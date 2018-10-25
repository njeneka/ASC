using System;
using AzureStorageTest.Interfaces;

namespace AzureStorageTest.Models
{
    public class BookEntity : BaseEntity, IAuditTracker
    {
        public BookEntity()
        {
        }

        public BookEntity(Guid id, string publisher)
        {
            Id = id;
            Publisher = publisher;

            RowKey = id.ToString();
            PartitionKey = publisher;
        }

        public Guid Id { get; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Publisher { get; set; }


    }
}