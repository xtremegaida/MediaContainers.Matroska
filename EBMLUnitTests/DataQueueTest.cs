using EBML;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EBMLUnitTests
{
   [TestClass]
   public class DataQueueTest
   {
      [TestMethod]
      public async Task BasicQueue()
      {
         using (var queue = new DataQueue())
         {
            await queue.WriteByteAsync(1);
            await queue.WriteByteAsync(2);
            await queue.WriteByteAsync(3);
            Assert.AreEqual(3, queue.UnreadLength);
            Assert.AreEqual(1, await queue.ReadByteAsync());
            Assert.AreEqual(2, await queue.ReadByteAsync());
            Assert.AreEqual(3, await queue.ReadByteAsync());
            Assert.AreEqual(0, queue.UnreadLength);
         }
      }

      [TestMethod]
      public async Task BasicArray()
      {
         using (var queue = new DataQueue())
         {
            await queue.WriteAsync(new byte[] { 1, 2, 3 });
            await queue.WriteAsync(new byte[] { 4, 5 });
            Assert.AreEqual(5, queue.UnreadLength);
            var buffer = new byte[5];
            var read = await queue.ReadAsync(new System.Memory<byte>(buffer, 0, 2), true);
            Assert.AreEqual(2, read);
            read = await queue.ReadAsync(new System.Memory<byte>(buffer, 2, 3), true);
            Assert.AreEqual(3, read);
            Assert.AreEqual(1, buffer[0]);
            Assert.AreEqual(2, buffer[1]);
            Assert.AreEqual(3, buffer[2]);
            Assert.AreEqual(4, buffer[3]);
            Assert.AreEqual(5, buffer[4]);
         }
      }

      [TestMethod]
      public async Task WaitForRead()
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
            var timeout = Task.Delay(100);
            var done = await Task.WhenAny(readTask, timeout);
            Assert.AreNotEqual(timeout, done);
            var read = await readTask;
            Assert.AreEqual(3, read);
            Assert.AreEqual(1, buffer[0]);
            Assert.AreEqual(2, buffer[1]);
            Assert.AreEqual(3, buffer[2]);
         }
      }

      [TestMethod]
      public async Task StreamInterfaces()
      {
         using (var queue = new DataQueue())
         {
            var testString = "test";
            using (var write = queue.WriteStream)
            using (var writer = new StreamWriter(write))
            {
               await writer.WriteAsync(testString);
            }
            Assert.IsTrue(queue.IsWriteClosed);
            using (var read = queue.ReadStream)
            using (var reader = new StreamReader(read))
            {
               var result = await reader.ReadToEndAsync();
               Assert.AreEqual(testString, result);
            }
            Assert.AreEqual(0, queue.UnreadLength);
            Assert.IsTrue(queue.IsReadClosed);
         }
      }
   }
}
