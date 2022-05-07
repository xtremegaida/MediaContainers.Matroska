using EBML;
using System;
using System.Threading.Tasks;

namespace EBMLTest
{
   class Program
   {
      static void Main(string[] args)
      {
         using (var queue = new DataQueue())
         {
            var buffer = new byte[3];
            var readTask = queue.ReadAsync(buffer, true).AsTask();
            _ = Task.Run(async () =>
            {
               await Task.Delay(1);
               await queue.WriteByteAsync(1);
               await Task.Delay(1);
               await queue.WriteAsync(new byte[] { 2, 3 });
            });
            //var timeout = Task.Delay(100);
            //var done = await Task.WhenAny(readTask, timeout);
            //Assert.AreNotEqual(timeout, done);
            var read = readTask.Result;
         }
      }
   }
}
