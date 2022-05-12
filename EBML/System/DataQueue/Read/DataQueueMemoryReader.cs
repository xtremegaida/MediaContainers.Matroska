using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EBML
{
   public class DataQueueMemoryReader : IDataQueueReader
   {
      private readonly ReadOnlyMemory<byte> memory;
      private volatile bool disposed;
      private int totalBytesRead;

      public long UnreadLength => memory.Length - totalBytesRead;
      public bool IsReadClosed => disposed || totalBytesRead >= memory.Length;
      public long TotalBytesRead => totalBytesRead;

      public DataQueueMemoryReader(ReadOnlyMemory<byte> memory)
      {
         this.memory = memory;
      }

      public ValueTask<int> ReadAsync(Memory<byte> buffer, bool waitUntilFull = false, CancellationToken cancellationToken = default)
      {
         if (disposed) { return ValueTask.FromResult(0); }
         var canRead = memory.Length - totalBytesRead;
         if (canRead > buffer.Length) { canRead = buffer.Length; }
         memory.Slice(totalBytesRead, canRead).CopyTo(buffer);
         Interlocked.Add(ref totalBytesRead, canRead);
         return ValueTask.FromResult(canRead);
      }

      public ValueTask<int> ReadAsync(int skipBytes, CancellationToken cancellationToken = default)
      {
         if (disposed) { return ValueTask.FromResult(0); }
         var canRead = memory.Length - totalBytesRead;
         if (canRead > skipBytes) { canRead = skipBytes; }
         Interlocked.Add(ref totalBytesRead, canRead);
         return ValueTask.FromResult(canRead);
      }

      public ValueTask<int> ReadByteAsync(CancellationToken cancellationToken = default)
      {
         if (disposed || totalBytesRead >= memory.Length) { return ValueTask.FromResult(-1); }
         var result = memory.Span[totalBytesRead];
         Interlocked.Increment(ref totalBytesRead);
         return ValueTask.FromResult((int)result);
      }

      public void Dispose()
      {
         disposed = true;
      }
   }
}
