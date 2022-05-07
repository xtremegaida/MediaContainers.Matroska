using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EBML
{
   public sealed class DataQueue : IDisposable
   {
      private readonly DataBufferCache cache;
      private readonly ConcurrentQueue<DataBuffer> bufferQueue = new ConcurrentQueue<DataBuffer>();
      private EventWaitLight readWait;
      private EventWaitLight writeWait;
      private EventWaitLight closedWait;
#if DEBUG
      private SpinLock readLock = new SpinLock(true);
      private SpinLock writeLock = new SpinLock(true);
#else
      private SpinLock readLock = new SpinLock(false);
      private SpinLock writeLock = new SpinLock(false);
#endif
      private volatile int currentCapacity;
      private int maxCapacity;
      private DataBuffer headRead;
      private DataBuffer tailWrite;
      private InternalReadStream readStream;
      private InternalWriteStream writeStream;
      private volatile bool closedRead;
      private volatile bool closedWrite;

      const int maxPacketSize = 65536;

      public int MaxCapacityBytes
      {
         get { return maxCapacity; }
         set { maxCapacity = value; writeWait.Trigger(); }
      }

      public int UnreadLength => currentCapacity;
      public bool IsReadClosed => closedRead;
      public bool IsWriteClosed => closedWrite;

      public Stream ReadStream => readStream ??= new InternalReadStream(this);
      public Stream WriteStream => writeStream ??= new InternalWriteStream(this);

      public DataQueue() : this(maxPacketSize * 4, null) { }
      public DataQueue(int maxCapacity, DataBufferCache cache = null)
      {
         this.maxCapacity = maxCapacity;
         this.cache = cache ?? DataBufferCache.DefaultCache;
      }

#region Read

      public async ValueTask<int> ReadByteAsync(CancellationToken cancellationToken = default)
      {
         bool locked = false;
         try
         {
            readLock.Enter(ref locked);
            while (true)
            {
               if (closedRead) { return -1; }
               if (headRead == null || headRead.ReadOffset >= headRead.Buffer.Length)
               {
                  headRead?.Dispose();
                  bufferQueue.TryDequeue(out headRead);
               }
               if (headRead == null || headRead.ReadOffset >= headRead.WriteOffset)
               {
                  if (closedWrite) { return -1; }
                  var task = readWait.WaitAsync(cancellationToken);
                  if (locked) { locked = false; readLock.Exit(); }
                  await task;
                  readLock.Enter(ref locked);
               }
               else
               {
                  var result = headRead.Buffer[headRead.ReadOffset++];
                  if (headRead.ReadOffset >= headRead.Buffer.Length) { headRead.Dispose(); headRead = null; }
                  Interlocked.Decrement(ref currentCapacity);
                  if (locked) { locked = false; readLock.Exit(); }
                  writeWait.Trigger();
                  return result;
               }
            }
         }
         finally
         {
            if (locked) { readLock.Exit(); }
         }
      }

      public async ValueTask<int> ReadAsync(Memory<byte> buffer, bool waitUntilFull = false, CancellationToken cancellationToken = default)
      {
         int readBytes = 0;
         bool locked = false;
         try
         {
            readLock.Enter(ref locked);
            while (!buffer.IsEmpty && !closedRead)
            {
               if (headRead == null || headRead.ReadOffset >= headRead.Buffer.Length)
               {
                  headRead?.Dispose();
                  bufferQueue.TryDequeue(out headRead);
               }
               if (headRead == null || headRead.ReadOffset >= headRead.WriteOffset)
               {
                  if ((!waitUntilFull && readBytes > 0) || closedWrite) { break; }
                  var task = readWait.WaitAsync(cancellationToken);
                  if (locked) { locked = false; readLock.Exit(); }
                  await task;
                  readLock.Enter(ref locked);
               }
               else
               {
                  var canRead = headRead.WriteOffset - headRead.ReadOffset;
                  if (canRead > buffer.Length) { canRead = buffer.Length; }
                  if (canRead > 0)
                  {
                     new Memory<byte>(headRead.Buffer, headRead.ReadOffset, canRead).CopyTo(buffer);
                     buffer = buffer.Slice(canRead);
                     headRead.ReadOffset += canRead;
                     readBytes += canRead;
                     Interlocked.Add(ref currentCapacity, -canRead);
                     if (headRead.ReadOffset >= headRead.Buffer.Length) { headRead.Dispose(); headRead = null; }
                  }
               }
            }
         }
         finally
         {
            if (locked) { readLock.Exit(); }
         }
         if (readBytes > 0) { writeWait.Trigger(); }
         return readBytes;
      }

#region Read Stream

      private class InternalReadStream : Stream
      {
         private readonly DataQueue owner;

         public override bool CanRead => true;
         public override bool CanSeek => false;
         public override bool CanWrite => false;
         public override long Length => owner.UnreadLength;
         public override long Position { get => 0; set => throw new NotSupportedException(); }

         public InternalReadStream(DataQueue owner) { this.owner = owner; }

         public override void Flush() { }
         public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
         public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
         public override void SetLength(long value) { throw new NotSupportedException(); }

         public override int Read(byte[] buffer, int offset, int count)
         {
            var task = owner.ReadAsync(new Memory<byte>(buffer, offset, count));
            if (task.IsCompletedSuccessfully) { return task.Result; }
            return task.AsTask().Result;
         }

         public override int ReadByte()
         {
            var task = owner.ReadByteAsync();
            if (task.IsCompletedSuccessfully) { return task.Result; }
            return task.AsTask().Result;
         }

         public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
         {
            var task = owner.ReadAsync(new Memory<byte>(buffer, offset, count), false, cancellationToken);
            if (task.IsCompletedSuccessfully) { return Task.FromResult(task.Result); }
            return task.AsTask();
         }

         public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
         {
            return owner.ReadAsync(buffer, false, cancellationToken);
         }

         public override void Close()
         {
            owner.Dispose();
         }
      }

#endregion

#endregion

#region Write

      public async ValueTask WriteByteAsync(byte data, CancellationToken cancellationToken = default)
      {
         bool locked = false;
         try
         {
            writeLock.Enter(ref locked);
            while (true)
            {
               if (closedWrite) { throw new InvalidOperationException(); }
               if (currentCapacity >= maxCapacity)
               {
                  var task = writeWait.WaitAsync(cancellationToken);
                  if (locked) { locked = false; writeLock.Exit(); }
                  readWait.Trigger();
                  await task;
                  writeLock.Enter(ref locked);
               }
               else
               {
                  if (tailWrite == null || tailWrite.WriteOffset >= tailWrite.Buffer.Length)
                  {
                     tailWrite = cache.Pop(256);
                     bufferQueue.Enqueue(tailWrite);
                  }
                  tailWrite.Buffer[tailWrite.WriteOffset++] = data;
                  Interlocked.Increment(ref currentCapacity);
                  if (locked) { locked = false; writeLock.Exit(); }
                  readWait.Trigger();
                  return;
               }
            }
         }
         finally
         {
            if (locked) { writeLock.Exit(); }
         }
      }

      public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
      {
         bool written = false;
         bool locked = false;
         try
         {
            writeLock.Enter(ref locked);
            while (!buffer.IsEmpty)
            {
               if (closedWrite) { throw new InvalidOperationException(); }
               if (currentCapacity >= maxCapacity)
               {
                  var task = writeWait.WaitAsync(cancellationToken);
                  if (locked) { locked = false; writeLock.Exit(); }
                  readWait.Trigger();
                  await task;
                  writeLock.Enter(ref locked);
               }
               else
               {
                  if (tailWrite == null || tailWrite.WriteOffset >= tailWrite.Buffer.Length)
                  {
                     var capacityLeft = maxCapacity - currentCapacity;
                     if (capacityLeft > buffer.Length) { capacityLeft = buffer.Length; }
                     tailWrite = cache.Pop(Math.Min(capacityLeft, maxPacketSize));
                     bufferQueue.Enqueue(tailWrite);
                  }
                  var canWrite = tailWrite.Buffer.Length - tailWrite.WriteOffset;
                  if (canWrite > buffer.Length) { canWrite = buffer.Length; }
                  if (canWrite > 0)
                  {
                     buffer.Slice(0, canWrite).CopyTo(new Memory<byte>(tailWrite.Buffer, tailWrite.WriteOffset, canWrite));
                     buffer = buffer.Slice(canWrite);
                     tailWrite.WriteOffset += canWrite;
                     Interlocked.Add(ref currentCapacity, canWrite);
                     written = true;
                  }
               }
            }
         }
         finally
         {
            if (locked) { writeLock.Exit(); }
         }
         if (written) { readWait.Trigger(); }
      }

#region Write Stream

      private class InternalWriteStream : Stream
      {
         private readonly DataQueue owner;

         public override bool CanRead => false;
         public override bool CanSeek => false;
         public override bool CanWrite => true;
         public override long Length => 0;
         public override long Position { get => 0; set => throw new NotSupportedException(); }

         public InternalWriteStream(DataQueue owner) { this.owner = owner; }

         public override void Flush() { }
         public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
         public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
         public override void SetLength(long value) { throw new NotSupportedException(); }

         public override void Write(byte[] buffer, int offset, int count)
         {
            var task = owner.WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count));
            if (task.IsCompletedSuccessfully) { return; }
            task.AsTask().Wait();
         }

         public override void WriteByte(byte value)
         {
            var task = owner.WriteByteAsync(value);
            if (task.IsCompletedSuccessfully) { return; }
            task.AsTask().Wait();
         }

         public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
         {
            var task = owner.WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken);
            if (task.IsCompletedSuccessfully) { return Task.CompletedTask; }
            return task.AsTask();
         }

         public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
         {
            return owner.WriteAsync(buffer, cancellationToken);
         }

         public override void Close()
         {
            owner.CloseWrite();
         }
      }

#endregion

#endregion

      public void Clear()
      {
         bool locked = false;
         try { writeLock.Enter(ref locked); tailWrite = null; }
         finally { if (locked) { locked = false; writeLock.Exit(); } }
         try
         {
            readLock.Enter(ref locked);
            if (headRead != null)
            {
               Interlocked.Add(ref currentCapacity, headRead.ReadOffset - headRead.WriteOffset);
               headRead.Dispose();
               headRead = null;
            }
            while (bufferQueue.TryDequeue(out var buffer))
            {
               Interlocked.Add(ref currentCapacity, buffer.ReadOffset - buffer.WriteOffset);
               buffer.Dispose();
            }
         }
         finally
         {
            if (locked) { readLock.Exit(); }
         }
         writeWait.Trigger();
      }

      public Task WaitForClose(CancellationToken cancellationToken = default)
      {
         return closedWait.WaitAsync(cancellationToken);
      }

      public void CloseWrite()
      {
         if (closedWrite) { return; }
         closedWrite = true;
         writeWait.Trigger();
         closedWait.Trigger(null, true);
         bool locked = false;
         writeLock.Enter(ref locked);
         if (locked) { writeLock.Exit(); }
      }

      public void Dispose()
      {
         if (closedRead) { return; }
         closedRead = true;
         CloseWrite();
         Clear();
         readWait.Trigger();
      }
   }
}
