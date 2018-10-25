using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AzureStorageTest.Interfaces;
using Microsoft.WindowsAzure.Storage.Table;

namespace AzureStorageTest
{
    public class UnitOfWork :IUnitOfWork, IDisposable
    {
        public UnitOfWork(string connectionString)
        {
            ConnectionString = connectionString;
            RollbackActions = new Queue<Task<Action>>();
            _repositories = new Dictionary<string, object>();
        }

        public string ConnectionString { get; set; }
        private bool _complete;
        private Dictionary<string, object> _repositories;

        public IRepository<T> Repository<T>() where T : TableEntity
        {
            // return repository from those in unit of work
            var table = typeof(T).Name;
            if (_repositories.ContainsKey(table))
            {
                return (IRepository<T>)_repositories[table];
            }

            var repositoryType = typeof(IRepository<>);
            var repository = Activator.CreateInstance(repositoryType.MakeGenericType(typeof(T)), this);
            _repositories.Add(table, repository);

            return (IRepository<T>)repository;
        }

        public void CommitTransaction()
        {
            _complete = true;
        }

        public Queue<Task<Action>> RollbackActions { get; set; }
        public void RollbackTransaction()
        {
            while (RollbackActions.Count > 0)
            {
                var action = RollbackActions.Dequeue();
                action.Result();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        ~UnitOfWork()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    if (!_complete)
                    {
                        RollbackTransaction();
                    }
                }
            }
            finally
            { 
                RollbackActions.Clear();
            }

            _complete = true;
        }
    }
}