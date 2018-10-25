using System;
using System.Threading.Tasks;
using AzureStorageTest.Models;

namespace AzureStorageTest
{
    class Program
    {
        private const string ConnectionString = "UseDevelopmentStorage=true;";

        static void Main()
        {
            var bookId = Guid.NewGuid();

            Task.Run(async () =>
            {
                using (var unitOfWork = new UnitOfWork(ConnectionString))
                {
                    var bookRepository = new Repository<BookEntity>(unitOfWork);
                    await bookRepository.CreateTableAsync();
                    var book = await bookRepository.InsertAsync(new BookEntity(bookId, "ABPress")
                    {
                        Author = "Jo Bloke",
                        Title = ".NET Core Journey"
                    });

                    Console.WriteLine(book);
                    unitOfWork.CommitTransaction();
                }

                using (var unitOfWork = new UnitOfWork(ConnectionString))
                {
                    var bookRepository = new Repository<BookEntity>(unitOfWork);
                    await bookRepository.CreateTableAsync();
                    var book = await bookRepository.FindAsync("ABPress", bookId.ToString());
                    book.Author = "Josephine Bloke";
                    var updatedBook = await bookRepository.UpdateAsync(book);

                    Console.WriteLine(updatedBook);
                    unitOfWork.CommitTransaction();
                }

                using (var unitOfWork = new UnitOfWork(ConnectionString))
                {
                    var bookRepository = new Repository<BookEntity>(unitOfWork);
                    await bookRepository.CreateTableAsync();
                    var book = await bookRepository.FindAsync("ABPress", bookId.ToString());

                    await bookRepository.DeleteAsync(book);
                    //throw new Exception("Rollback Deleted");
                    Console.WriteLine("Deleted");
                    unitOfWork.CommitTransaction();
                }

            }).GetAwaiter().GetResult();
        }
    }
}
