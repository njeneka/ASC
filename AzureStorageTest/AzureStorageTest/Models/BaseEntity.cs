using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace AzureStorageTest.Models
{
    public class BaseEntity : TableEntity
    {
        public bool IsDeleted { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedDate { get; set; }
    }
}