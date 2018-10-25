using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

namespace AzureStorageTest.Interfaces
{
    public interface IUnitOfWork
    {
        string ConnectionString { get; set; }
        IRepository<T> Repository<T>() where T : TableEntity;
        void CommitTransaction();
        Queue<Task<Action>> RollbackActions { get; set; }

        void RollbackTransaction();
    }
}