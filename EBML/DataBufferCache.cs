using System;
using System.Collections.Concurrent;
using System.Threading;

namespace EBML
{
   public class DataBufferCache
   {
      public static readonly DataBufferCache DefaultCache = new DataBufferCache();

      private readonly ConcurrentQueue<DataBuffer> bucket = new ConcurrentQueue<DataBuffer>();
      private long memReserve;
      private long memReserveMax = 32 << 20; // 32MiB
      private long memUsed;
      private int memMinSize = 256;
      private int memMaxSize = 262144;

      public int BufferMinSize
      {
         get { return memMinSize; }
         set
         {
            var size = value;
            if (size <= 16) { size = 16; }
            if (size > 65536) { size = 65536; }
            memMinSize = size;
            var itemCount = bucket.Count;
            while (--itemCount >= 0 && bucket.TryDequeue(out var buffer))
            {
               if (buffer.Buffer.Length >= size) { bucket.Enqueue(buffer); }
               else { Interlocked.Add(ref memReserve, -buffer.Buffer.Length); }
            }
         }
      }

      public int BufferMaxSize
      {
         get { return memMaxSize; }
         set
         {
            var size = value;
            if (size <= 256) { size = 256; }
            memMaxSize = size;
            var itemCount = bucket.Count;
            while (--itemCount >= 0 && bucket.TryDequeue(out var buffer))
            {
               if (buffer.Buffer.Length <= size) { bucket.Enqueue(buffer); }
               else { Interlocked.Add(ref memReserve, -buffer.Buffer.Length); }
            }
         }
      }

      public long BufferMemoryReserveBytes => Interlocked.Read(ref memReserve);
      public long BufferMemoryUsedBytes => Interlocked.Read(ref memUsed);
      public long BufferMemoryMaxBytes
      {
         get { return Interlocked.Read(ref memReserveMax); }
         set
         {
            Interlocked.Exchange(ref memReserveMax, value);
            if (BufferMemoryUsedBytes > BufferMemoryMaxBytes)
            {
               while (BufferMemoryUsedBytes > BufferMemoryMaxBytes && bucket.TryDequeue(out var buffer))
               {
                  Interlocked.Add(ref memReserve, -buffer.Buffer.Length);
               }
            }
         }
      }

      internal void _PushInternal(DataBuffer buffer)
      {
         if (buffer.References != 0) { throw new InvalidOperationException(); }
         var length = buffer.Buffer.Length;
         if (length < memMinSize || length > memMaxSize) { return; }
         if ((BufferMemoryUsedBytes + length) > BufferMemoryMaxBytes) { return; }
         bucket.Enqueue(buffer);
         Interlocked.Add(ref memReserve, length);
         Interlocked.Add(ref memUsed, -length);
      }

      public DataBuffer Pop(int minLength)
      {
         var itemCount = bucket.Count;
         while (--itemCount >= 0 && bucket.TryDequeue(out var buffer))
         {
            var length = buffer.Buffer.Length;
            if (length < minLength) { bucket.Enqueue(buffer); }
            else
            {
               Interlocked.Add(ref memReserve, -length);
               Interlocked.Add(ref memUsed, length);
               System.Diagnostics.Debug.Assert(buffer.References == 0);
               buffer._ResetInternal();
               return buffer;
            }
         }
         if (minLength < memMinSize) { minLength = memMinSize; }
         Interlocked.Add(ref memUsed, minLength);
         return new DataBuffer(this, new byte[minLength]);
      }

      public void Clear()
      {
         while (bucket.TryDequeue(out var buffer))
         {
            Interlocked.Add(ref memReserve, -buffer.Buffer.Length);
         }
      }
   }

   public sealed class DataBuffer : IDisposable
   {
      public readonly DataBufferCache Owner;
      public readonly byte[] Buffer;
      public int ReadOffset;
      public int WriteOffset;

      private int refCount;

      public int References => refCount;

      internal DataBuffer(DataBufferCache owner, byte[] buffer)
      {
         if (buffer == null) { throw new ArgumentNullException("buffer"); }
         Owner = owner;
         Buffer = buffer;
         refCount = 1;
      }

      public void AddReference()
      {
         Interlocked.Increment(ref refCount);
      }

      internal void _ResetInternal()
      {
         ReadOffset = 0;
         WriteOffset = 0;
         refCount = 1;
      }

      public void Dispose()
      {
         if (Interlocked.Decrement(ref refCount) <= 0)
         {
            ReadOffset = 0;
            WriteOffset = 0;
            Owner._PushInternal(this);
         }
      }
   }
}
