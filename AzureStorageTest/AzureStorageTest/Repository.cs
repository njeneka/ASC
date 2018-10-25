using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using AzureStorageTest.Extensions;
using AzureStorageTest.Interfaces;
using AzureStorageTest.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace AzureStorageTest
{
    public class Repository<T> : IRepository<T> where T : TableEntity, new()
    {
        private readonly IUnitOfWork _scope;
        private readonly CloudStorageAccount _cloudStorageAccount;
        private readonly CloudTableClient _cloudTableClient;
        private readonly CloudTable _cloudTable;

        public Repository(IUnitOfWork scope)
        {
            _scope = scope;

            _cloudStorageAccount = CloudStorageAccount.Parse(scope.ConnectionString);
            _cloudTableClient = _cloudStorageAccount.CreateCloudTableClient();
            _cloudTable = _cloudTableClient.GetTableReference(typeof(T).Name);
        }

        public async Task DeleteAsync(T entity)
        {
            if (entity is BaseEntity baseEntity)
            {
                baseEntity.ModifiedDate = DateTime.UtcNow;
                baseEntity.IsDeleted = true;
            }
            await ExecuteAsync(TableOperationType.Delete, entity);
        }

        public async Task<IEnumerable<T>> FindAllAsync(string partitionKey)
        {
            var query = new TableQuery<T>().Where(
                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey));
            var items = new List<T>();

            TableContinuationToken continuationToken = null;
            do
            {
                var result = await _cloudTable.ExecuteQuerySegmentedAsync(query, continuationToken);
                continuationToken = result.ContinuationToken;
                items.AddRange(result.Results);
            } while (continuationToken != null);

            return items.AsEnumerable();
        }

        public async Task<T> FindAsync(string partitionKey, string rowKey)
        {
            var result = await _cloudTable.ExecuteAsync(TableOperation.Retrieve<T>(partitionKey, rowKey));

            return result.Result as T;
        }

        public async Task<T> InsertAsync(T entity)
        {
            if (entity is BaseEntity baseEntity)
            {
                baseEntity.CreatedDate = DateTime.UtcNow;
                baseEntity.ModifiedDate = DateTime.UtcNow;
            }
            var result = await ExecuteAsync(TableOperationType.Insert, entity);
            return result.Result as T;
        }

        public async Task<T> UpdateAsync(T entity)
        {
            if (entity is BaseEntity baseEntity)
            {
                baseEntity.ModifiedDate = DateTime.UtcNow;
            }
            var result = await ExecuteAsync(TableOperationType.Replace, entity);
            return result.Result as T;
        }

        public async Task CreateTableAsync()
        {
            var table = _cloudTableClient.GetTableReference(typeof(T).Name);
            await table.CreateIfNotExistsAsync();

            if (typeof(IAuditTracker).IsAssignableFrom(typeof(T)))
            {
                var auditTable = _cloudTableClient.GetTableReference($"{typeof(T).Name}Audit");
                await auditTable.CreateIfNotExistsAsync();
            }
        }

        private async Task<TableResult> ExecuteAsync(TableOperationType operationType, T entity)
        {
            var operation = operationType == TableOperationType.Insert
                ? TableOperation.Insert(entity)
                : TableOperation.Replace(entity);
            var rollbackOperation = CreateRollbackAction(operationType, entity);

            var result = await _cloudTable.ExecuteAsync(operation);
            _scope.RollbackActions.Enqueue(rollbackOperation);

            if (entity is IAuditTracker)
            {
                // record audit and rollback
                var auditEntity = entity.CopyObject<T>();
                // need new partition and row keys for each audit entry
                auditEntity.PartitionKey = $"{entity.PartitionKey}-{entity.RowKey}";
                auditEntity.RowKey = $"{DateTime.UtcNow:yyyy-mm-ddThh:mm:ss:fff}";

                var auditRollbackOperation = CreateRollbackAction(TableOperationType.Insert, auditEntity, true);
                _scope.RollbackActions.Enqueue(auditRollbackOperation);

                var auditTable = _cloudTableClient.GetTableReference($"{typeof(T).Name}Audit");
                await auditTable.ExecuteAsync(TableOperation.Insert(auditEntity));
            }
            return result;
        }

        private async Task<Action> CreateRollbackAction(TableOperationType operationType, T entity, bool isAudit = false)
        {
            var table = isAudit ? _cloudTableClient.GetTableReference($"{typeof(T).Name}Audit") : _cloudTable;
            switch (operationType)
            {
                case TableOperationType.Delete:
                    return async () => await UndoDeleteOperationAsync(table, entity);
                case TableOperationType.Insert:
                    return async () => await UndoInsertOperationAsync(table, entity);
                case TableOperationType.Replace:
                    // get the original version
                    var retrieveResult =
                        await table.ExecuteAsync(TableOperation.Retrieve(entity.PartitionKey, entity.RowKey));

                    return async () => await UndoReplaceOperationAsync(table, retrieveResult.Result as DynamicTableEntity, entity);
                default:
                    throw new InvalidOperationException("The storage operation cannot be identified");
            }
        }

        private async Task UndoDeleteOperationAsync(CloudTable table, ITableEntity entity)
        {
            if (entity is BaseEntity baseEntity)
            {
                baseEntity.IsDeleted = false;
            }

            var operation = TableOperation.Replace(entity);
            await table.ExecuteAsync(operation);
        }

        private async Task UndoInsertOperationAsync(CloudTable table, ITableEntity entity)
        {
            var operation = TableOperation.Delete(entity);
            await table.ExecuteAsync(operation);
        }

        private async Task UndoReplaceOperationAsync(CloudTable table, ITableEntity originalEntity, ITableEntity entity)
        {
            if (originalEntity != null)
            {
                if (!string.IsNullOrEmpty(entity.ETag))
                    originalEntity.ETag = entity.ETag;

                var operation = TableOperation.Replace(originalEntity);
                await table.ExecuteAsync(operation);
            }
        }
    }
}