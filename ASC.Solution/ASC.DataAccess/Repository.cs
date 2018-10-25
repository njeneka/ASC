using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using ASC.DataAccess.Extensions;
using ASC.DataAccess.Interfaces;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace ASC.DataAccess
{
    public class Repository<T> : IRepository<T> where T : TableEntity, new()
    {
        private readonly CloudStorageAccount _storageAccount;
        private readonly CloudTableClient _tableClient;
        private readonly CloudTable _storageTable;

        public Repository(IUnitOfWork scope)
        {
            _storageAccount = CloudStorageAccount.Parse(scope.ConnectionString);
            _tableClient = _storageAccount.CreateCloudTableClient();
            _storageTable = _tableClient.GetTableReference(typeof(T).Name);

            Scope = scope;
        }

        public IUnitOfWork Scope { get; set; }

        public async Task<T> AddAsync(T entity)
        {
            if (entity is BaseEntity entityToInsert)
            {
                entityToInsert.CreatedDate = DateTime.UtcNow;
                entityToInsert.UpdatedDate = DateTime.UtcNow;
            }

            var result = await ExecuteAsync(TableOperationType.Insert, entity);
            return result.Result as T;
        }

        public async Task<T> UpdateAsync(T entity)
        {
            if (entity is BaseEntity entityToUpdate)
            {
                entityToUpdate.UpdatedDate = DateTime.UtcNow;
            }

            var result = await ExecuteAsync(TableOperationType.Replace, entity);
            return result.Result as T;
        }

        public async Task DeleteAsync(T entity)
        {
            if (entity is BaseEntity entityToDelete)
            {
                entityToDelete.UpdatedDate = DateTime.UtcNow;
                entityToDelete.IsDeleted = true;
            }

            await ExecuteAsync(TableOperationType.Replace, entity);
        }

        public async Task<T> FindAsync(string partitionKey, string rowKey)
        {
            var retrieveOperation = TableOperation.Retrieve<T>(partitionKey, rowKey);
            var result = await _storageTable.ExecuteAsync(retrieveOperation);

            return result.Result as T;
        }

        public async Task<IEnumerable<T>> FindAllByPartitionKeyAsync(string partitionKey)
        {
            var query = new TableQuery<T>().Where(
                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey));
            var result = await _storageTable.ExecuteQuerySegmentedAsync(query, null);

            return result.Results.AsEnumerable();
        }

        public async Task<IEnumerable<T>> FindAllAsync()
        {
            var query = new TableQuery<T>();
            var result = await _storageTable.ExecuteQuerySegmentedAsync(query, null);

            return result.Results.AsEnumerable();
        }

        public async Task CreateTableAsync()
        {
            var table = _tableClient.GetTableReference(typeof(T).Name);
            await table.CreateIfNotExistsAsync();

            if (typeof(IAuditTracker).IsAssignableFrom(typeof(T)))
            {
                // create audit table
                var auditTracker = _tableClient.GetTableReference($"{typeof(T).Name}Audit");
                await auditTracker.CreateIfNotExistsAsync();
            }
        }

        private async Task<TableResult> ExecuteAsync(TableOperationType operationType, T entity)
        {
            var rollbackAction = CreateRollbackAction(operationType, entity);
            var operation = operationType == TableOperationType.Insert
                ? TableOperation.Insert(entity)
                : TableOperation.Replace(entity);
            var result = await _storageTable.ExecuteAsync(operation);

            Scope.RollbackActions.Enqueue(rollbackAction);

            if (entity is IAuditTracker)
            {
                // new audit entry
                var auditEntity = entity.CopyObject<T>();
                auditEntity.PartitionKey = $"{auditEntity.PartitionKey}-{auditEntity.RowKey}";
                auditEntity.RowKey = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff}";

                var auditOperation = TableOperation.Insert(auditEntity);
                var auditRollbackAction = CreateRollbackAction(TableOperationType.Insert, auditEntity, true);
                var auditTable = _tableClient.GetTableReference($"{typeof(T).Name}Audit");
                await auditTable.ExecuteAsync(auditOperation);
                Scope.RollbackActions.Enqueue(auditRollbackAction);

            }
            return result;
        }

        private async Task<Action> CreateRollbackAction(TableOperationType operationType, T entity, bool isAuditOperation = false)
        {
            var cloudTable = isAuditOperation ? _tableClient.GetTableReference($"{typeof(T).Name}Audit") : _storageTable;

            switch (operationType)
            {
                case TableOperationType.Insert:
                    return async () => await UndoInsertOperationAsync(cloudTable, entity);

                case TableOperationType.Delete:
                    return async () => await UndoDeleteOperationAsync(cloudTable, entity);

                case TableOperationType.Replace:
                    var retrieveResult =
                        await cloudTable.ExecuteAsync(TableOperation.Retrieve(entity.PartitionKey, entity.RowKey));

                    return async () =>
                        await UndoReplaceOperationAsync(cloudTable, retrieveResult.Result as DynamicTableEntity,
                            entity);

                default:
                    throw new InvalidOperationException("Unhandled storage operation");

            }
        }

        private static async Task UndoInsertOperationAsync(CloudTable table, ITableEntity entity)
        {
            var deleteOperation = TableOperation.Delete(entity);
            await table.ExecuteAsync(deleteOperation);
        }

        private static async Task UndoReplaceOperationAsync(CloudTable table, ITableEntity originalEntity,
            ITableEntity entity)
        {
            if (originalEntity != null)
            {
                // ETag is used for optimistic concurrency - set original to current so it overwrites
                if (!string.IsNullOrEmpty(entity.ETag))
                    originalEntity.ETag = entity.ETag;

                var replaceOperation = TableOperation.Replace(originalEntity);
                await table.ExecuteAsync(replaceOperation);
            }
        }

        private static async Task UndoDeleteOperationAsync(CloudTable table, ITableEntity entity)
        {
            if (entity is BaseEntity entityToRestore)
            {
                entityToRestore.IsDeleted = false;
            }

            var insertOperation = TableOperation.Replace(entity);
            await table.ExecuteAsync(insertOperation);
        }
    }

}