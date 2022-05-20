using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Xtremegaida.DataStructures.UnitTests
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

      private async Task DataFlowTest(int bufferSize, int boundA, int boundB, bool includeStreams, bool useForward)
      {
         var cache = new DataBufferCache();
         using (var queue1 = new DataQueue(boundA, cache))
         using (var queue2 = new DataQueue(boundB, cache))
         {
            var sourceQueue = queue1;
            var targetQueue = includeStreams ? queue2 : queue1;
            var buffer1 = new byte[bufferSize];
            var buffer2 = new byte[bufferSize];
            int writeBytes = 0, readBytes = 0;
            var rnd = new Random();
            rnd.NextBytes(buffer1);
            var read = Task.Run(async () =>
            {
               var rnd = new Random();
               int read = 0;
               while (!targetQueue.IsWriteClosed || targetQueue.UnreadLength > 0)
               {
                  var canRead = bufferSize - read;
                  var size = rnd.Next(2048, 4096);
                  if (canRead > size) { canRead = size; }
                  if (canRead == 0) { Assert.AreEqual(0, targetQueue.UnreadLength); break; }
                  var bytesRead = await targetQueue.ReadAsync(new Memory<byte>(buffer2, read, canRead));
                  if (bytesRead == 0 && (!targetQueue.IsWriteClosed || targetQueue.UnreadLength > 0)) { throw new Exception("Unexpected End of Stream"); }
                  read += bytesRead;
                  readBytes += bytesRead;
               }
            });
            var write = Task.Run(async () =>
            {
               var rnd = new Random();
               int written = 0;
               int wait = 32;
               while (written < bufferSize)
               {
                  var canWrite = bufferSize - written;
                  var size = rnd.Next(2048, 4096);
                  if (canWrite > size) { canWrite = size; }
                  await sourceQueue.WriteAsync(new ReadOnlyMemory<byte>(buffer1, written, canWrite));
                  if (--wait == 0) { await Task.Delay(1); }
                  written += canWrite;
                  writeBytes += canWrite;
               }
               sourceQueue.CloseWrite();
            });
            if (includeStreams)
            {
               var pipe = Task.Run(async () =>
               {
                  await Task.Delay(1);
                  if (useForward)
                  {
                     await sourceQueue.ForwardTo(targetQueue);
                     targetQueue.CloseWrite();
                  }
                  else
                  {
                     using (var readStream = sourceQueue.ReadStream)
                     using (var writeStream = targetQueue.WriteStream)
                     {
                        await readStream.CopyToAsync(writeStream);
                     }
                  }
               });
               await pipe;
            }
            await write;
            await read;
            Assert.AreEqual(writeBytes, readBytes);
            Assert.AreEqual(writeBytes, bufferSize);
            Assert.AreEqual(writeBytes, sourceQueue.TotalBytesWritten);
            Assert.AreEqual(readBytes, targetQueue.TotalBytesRead);
            bool match = true;
            for (int i = 0; i < bufferSize; i++) { if (buffer1[i] != buffer2[i]) { match = false; break; } }
            Assert.IsTrue(match);
         }
         Assert.AreEqual(0, cache.BufferMemoryUsedBytes);
      }

      [TestMethod]
      public Task DataSinglePipingUnbounded() { return DataFlowTest(10 << 20, 20 << 20, 20 << 20, false, false); }

      [TestMethod]
      public Task DataDoublePipingUnbounded() { return DataFlowTest(10 << 20, 20 << 20, 20 << 20, true, false); }

      [TestMethod]
      public Task DataDoublePipingUnboundedForwarded() { return DataFlowTest(10 << 20, 20 << 20, 20 << 20, true, true); }

      [TestMethod]
      public Task DataSinglePipingBounded() { return DataFlowTest(10 << 20, 8000, 5000, false, false); }

      [TestMethod]
      public Task DataDoublePipingBounded() { return DataFlowTest(10 << 20, 8000, 5000, true, false); }

      [TestMethod]
      public Task DataDoublePipingBoundedForwarded() { return DataFlowTest(10 << 20, 8000, 5000, true, true); }

      [TestMethod]
      public async Task QueueSnapshot()
      {
         var bufferSize = 1 << 20;
         var cache = new DataBufferCache();
         using (var queue = new DataQueue(bufferSize, cache))
         {
            var rnd = new Random();
            var src = new byte[bufferSize];
            rnd.NextBytes(src);

            await queue.WriteAsync(src).AsTask().WaitAsync(1000);
            Assert.AreEqual(src.Length, queue.TotalBytesWritten);
            Assert.AreEqual(src.Length, queue.UnreadLength);
            var snapshot = queue.ToArray(true);
            Assert.AreEqual(0, queue.TotalBytesWritten);
            Assert.AreEqual(0, queue.UnreadLength);
            Assert.AreEqual(src.Length, snapshot.Length);
            bool match = true;
            for (int i = 0; i < src.Length; i++) { if (src[i] != snapshot[i]) { match = false; break; } }
            Assert.IsTrue(match);

            rnd.NextBytes(src);
            await queue.WriteAsync(src).AsTask().WaitAsync(1000);
            Assert.AreEqual(src.Length, queue.TotalBytesWritten);
            Assert.AreEqual(src.Length, queue.UnreadLength);
            snapshot = queue.ToArray(false);
            Assert.AreEqual(src.Length, queue.TotalBytesWritten);
            Assert.AreEqual(src.Length, queue.UnreadLength);
            Assert.AreEqual(src.Length, snapshot.Length);
            match = true;
            for (int i = 0; i < src.Length; i++) { if (src[i] != snapshot[i]) { match = false; break; } }
            Assert.IsTrue(match);

            var read = new byte[(int)queue.TotalBytesWritten];
            await queue.ReadAsync(read, true).AsTask().WaitAsync(1000);
            Assert.AreEqual(src.Length, read.Length);
            match = true;
            for (int i = 0; i < src.Length; i++) { if (src[i] != read[i]) { match = false; break; } }
            Assert.IsTrue(match);
         }
      }
   }
}
