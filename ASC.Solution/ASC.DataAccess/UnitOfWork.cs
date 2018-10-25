using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ASC.DataAccess.Interfaces;
using Microsoft.WindowsAzure.Storage.Table;

namespace ASC.DataAccess
{
    public class UnitOfWork :IUnitOfWork
    {
        private bool _complete;
        private Dictionary<string, object> _repositories;

        public UnitOfWork(string connectionString)
        {
            ConnectionString = connectionString;
            RollbackActions = new Queue<Task<Action>>();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (!_complete)
                    {
                        RollbackTransaction();
                    }
                }
                finally
                {
                    RollbackActions.Clear();
                }
            }

            _complete = false;
        }

        public Queue<Task<Action>> RollbackActions { get; set; }
        public string ConnectionString { get; set; }
        public IRepository<T> Repository<T>() where T : TableEntity
        {
            if (_repositories == null)
            {
                _repositories = new Dictionary<string, object>();
            }

            var type = typeof(T).Name;
            if (_repositories.ContainsKey(type))
            {
                return _repositories[type] as IRepository<T>;
            }

            var repositoryType = typeof(Repository<>);
            var repositoryInstance = Activator.CreateInstance(repositoryType.MakeGenericType(typeof(T)), this);
            _repositories.Add(type, repositoryInstance);

            return _repositories[type] as IRepository<T>;
        }

        public void CommitTransaction()
        {
            _complete = true;
        }

        private void RollbackTransaction()
        {
            while (RollbackActions.Count > 0)
            {
                var action = RollbackActions.Dequeue();
                action.Result();
            }
        }

        ~UnitOfWork()
        {
            Dispose(false);
        }
    }
}