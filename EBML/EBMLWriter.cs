using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EBML
{
   public sealed class EBMLWriter : IDisposable, IAsyncDisposable
   {
      private readonly DataBufferCache cache;
      private readonly IDataQueueWriter writer;
      private readonly Stream stream;
      private readonly bool keepWriterOpen;
      private readonly List<Level> masterBlocks = new();
      private volatile bool disposed;
      private Level level;

      private class Level : IDisposable
      {
         public readonly EBMLElementDefiniton Definition;
         public readonly DataQueue Buffer;
         public readonly IDataQueueWriter ParentWriter;
         public Task ForwardTask;

         public Level(EBMLElementDefiniton def, DataQueue buffer, IDataQueueWriter parent)
         {
            Definition = def;
            Buffer = buffer;
            ParentWriter = parent;
            buffer.OnBlockWrite += ActivateStreamingMode;
         }

         public void ActivateStreamingMode()
         {
            if (ForwardTask != null) { return; }
            ForwardTask = ForwardAsync();
         }

         private async Task ForwardAsync()
         {
            await EBMLVInt.CreateUnknown(Definition.AllowUnknownSize ? 1 : 8).Write(ParentWriter);
            await Buffer.ForwardTo(ParentWriter);
         }

         public void Dispose()
         {
            Buffer.Dispose();
         }
      }

      public EBMLElementDefiniton CurrentContainer => level?.Definition;

      public EBMLWriter(IDataQueueWriter writer, bool keepWriterOpen = false, DataBufferCache cache = null)
      {
         this.writer = writer;
         this.keepWriterOpen = keepWriterOpen;
         this.cache = cache ?? DataBufferCache.DefaultCache;
      }

      public EBMLWriter(Stream stream, bool keepStreamOpen = false, DataBufferCache cache = null)
         : this(new DataQueueStreamWriter(stream, keepStreamOpen), false, cache)
      {
         this.stream = stream;
      }

      public static int CalculateWidth(long value)
      {
         if (value == 0) { return 0; }
         for (int size = 1; size < 8; size++)
         {
            var shift = 64 - (size << 3);
            if (((value << shift) >> shift) == value) { return size; }
         }
         return 8;
      }

      public static int CalculateWidth(ulong value)
      {
         if (value == 0) { return 0; }
         for (int size = 1; size < 8; size++)
         {
            var mask = (ulong)(-1L << (size << 3));
            if ((mask & value) == 0) { return size; }
         }
         return 8;
      }

      public async ValueTask WriteSignedInteger(EBMLElementDefiniton def, long value, CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (def == null) { throw new ArgumentNullException(nameof(def)); }
         if (def.Type != EBMLElementType.SignedInteger) { throw new ArgumentException("Element type mismatch", nameof(def)); }

         var size = new EBMLVInt((ulong)CalculateWidth(value));
         await def.Id.Write(writer, cancellationToken);
         await size.Write(writer, cancellationToken);
         if (size.Value == 0) { return; }
         for (int i = (int)(size.Value - 1) << 3; i >= 0; i -= 8) { await writer.WriteByteAsync((byte)((value >> i) & 0xff), cancellationToken); }
      }

      public async ValueTask WriteUnsignedInteger(EBMLElementDefiniton def, ulong value, CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (def == null) { throw new ArgumentNullException(nameof(def)); }
         if (def.Type != EBMLElementType.UnsignedInteger) { throw new ArgumentException("Element type mismatch", nameof(def)); }

         var size = new EBMLVInt((ulong)CalculateWidth(value));
         await def.Id.Write(writer, cancellationToken);
         await size.Write(writer, cancellationToken);
         if (size.Value == 0) { return; }
         for (int i = (int)(size.Value - 1) << 3; i >= 0; i -= 8) { await writer.WriteByteAsync((byte)((value >> i) & 0xff), cancellationToken); }
      }

      public async ValueTask WriteFloat(EBMLElementDefiniton def, float value, CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (def == null) { throw new ArgumentNullException(nameof(def)); }
         if (def.Type != EBMLElementType.Float) { throw new ArgumentException("Element type mismatch", nameof(def)); }

         var size = new EBMLVInt(4);
         await def.Id.Write(writer, cancellationToken);
         await size.Write(writer, cancellationToken);
         using (var tmp = cache.Pop(4))
         {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(tmp.Buffer, value);
            await writer.WriteAsync(new ReadOnlyMemory<byte>(tmp.Buffer, 0, 4), cancellationToken);
         }
      }

      public async ValueTask WriteFloat(EBMLElementDefiniton def, double value, CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (def == null) { throw new ArgumentNullException(nameof(def)); }
         if (def.Type != EBMLElementType.Float) { throw new ArgumentException("Element type mismatch", nameof(def)); }

         var size = new EBMLVInt(8);
         await def.Id.Write(writer, cancellationToken);
         await size.Write(writer, cancellationToken);
         using (var tmp = cache.Pop(8))
         {
            System.Buffers.Binary.BinaryPrimitives.WriteDoubleBigEndian(tmp.Buffer, value);
            await writer.WriteAsync(new ReadOnlyMemory<byte>(tmp.Buffer, 0, 8), cancellationToken);
         }
      }

      public async ValueTask WriteDate(EBMLElementDefiniton def, DateTime date, CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (def == null) { throw new ArgumentNullException(nameof(def)); }
         if (def.Type != EBMLElementType.Date) { throw new ArgumentException("Element type mismatch", nameof(def)); }

         var size = new EBMLVInt(8);
         await def.Id.Write(writer, cancellationToken);
         await size.Write(writer, cancellationToken);
         using (var tmp = cache.Pop(8))
         {
            var value = (date.Ticks - EBMLDateElement.Epoch.Ticks) * 100;
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(tmp.Buffer, value);
            await writer.WriteAsync(new ReadOnlyMemory<byte>(tmp.Buffer, 0, 8), cancellationToken);
         }
      }

      public async ValueTask WriteString(EBMLElementDefiniton def, string str, CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (def == null) { throw new ArgumentNullException(nameof(def)); }
         if (def.Type != EBMLElementType.String && def.Type != EBMLElementType.UTF8) { throw new ArgumentException("Element type mismatch", nameof(def)); }

         if (str == null) { str = string.Empty; }
         using (var tmp = cache.Pop(str.Length * 4))
         {
            var bytes = (def.Type == EBMLElementType.UTF8 ? System.Text.Encoding.UTF8 : System.Text.Encoding.ASCII).GetBytes(str, tmp.Buffer);
            var size = new EBMLVInt((ulong)bytes);
            await def.Id.Write(writer, cancellationToken);
            await size.Write(writer, cancellationToken);
            await writer.WriteAsync(new ReadOnlyMemory<byte>(tmp.Buffer, 0, bytes), cancellationToken);
         }
      }

      public async ValueTask WriteBinary(EBMLElementDefiniton def, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (def == null) { throw new ArgumentNullException(nameof(def)); }
         if (def.Type != EBMLElementType.Binary && def.Type != EBMLElementType.Unknown) { throw new ArgumentException("Element type mismatch", nameof(def)); }

         var size = new EBMLVInt((ulong)bytes.Length);
         await def.Id.Write(writer, cancellationToken);
         await size.Write(writer, cancellationToken);
         await writer.WriteAsync(bytes, cancellationToken);
      }

      public async ValueTask WriteBinary(EBMLElementDefiniton def, IDataQueue bytes, CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (def == null) { throw new ArgumentNullException(nameof(def)); }
         if (bytes == null) { throw new ArgumentNullException(nameof(bytes)); }
         if (def.Type != EBMLElementType.Binary && def.Type != EBMLElementType.Unknown) { throw new ArgumentException("Element type mismatch", nameof(def)); }

         await def.Id.Write(writer, cancellationToken);
         if (bytes.IsWriteClosed)
         {
            await new EBMLVInt((ulong)bytes.UnreadLength).Write(writer, cancellationToken);
            if (bytes is DataQueue dq) { await dq.ForwardTo(writer, cancellationToken); }
            else
            {
               using (var target = new DataQueueWriteStream(writer, true))
               {
                  await bytes.ReadStream.CopyToAsync(target, cancellationToken);
               }
            }
         }
         else if (!(stream?.CanSeek ?? false)) { await WriteBinary(def, bytes.ReadStream); }
         else
         {
            await EBMLVInt.CreateUnknown(8).Write(writer, cancellationToken);
            long start = writer.TotalBytesWritten;
            if (bytes is DataQueue dq) { await dq.ForwardTo(writer, cancellationToken); }
            else
            {
               using (var target = new DataQueueWriteStream(writer, true))
               {
                  await bytes.ReadStream.CopyToAsync(target, cancellationToken);
               }
            }
            await FlushAsync(cancellationToken);
            await WriteSizeOnStream(writer.TotalBytesWritten - start, cancellationToken);
         }
      }

      public async ValueTask WriteBinary(EBMLElementDefiniton def, Stream bytes, CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (def == null) { throw new ArgumentNullException(nameof(def)); }
         if (bytes == null) { throw new ArgumentNullException(nameof(bytes)); }
         if (def.Type != EBMLElementType.Binary && def.Type != EBMLElementType.Unknown) { throw new ArgumentException("Element type mismatch", nameof(def)); }

         await def.Id.Write(writer, cancellationToken);
         if (bytes.CanSeek)
         {
            var size = new EBMLVInt((ulong)(bytes.Length - bytes.Position));
            await size.Write(writer, cancellationToken);
            if (writer is IDataQueue dq) { await bytes.CopyToAsync(dq.WriteStream, cancellationToken); }
            else
            {
               using (var target = new DataQueueWriteStream(writer, true))
               {
                  await bytes.CopyToAsync(target, cancellationToken);
               }
            }
         }
         else
         {
            using (var mem = new MemoryStream())
            {
               await bytes.CopyToAsync(mem, cancellationToken);
               await new EBMLVInt((ulong)mem.Length).Write(writer, cancellationToken);
               mem.Position = 0;
               using (var target = new DataQueueWriteStream(writer, true)) { await mem.CopyToAsync(target, cancellationToken); }
            }
         }
      }

      public async ValueTask WriteElement(EBMLElement element, int bufferSize = 256, CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (element == null) { throw new ArgumentNullException(nameof(element)); }
         switch (element.ElementType)
         {
            case EBMLElementType.SignedInteger:
               await WriteSignedInteger(element.Definition, element.IntValue, cancellationToken); 
               break;
            case EBMLElementType.UnsignedInteger:
               await WriteUnsignedInteger(element.Definition, element.UIntValue, cancellationToken);
               break;
            case EBMLElementType.Float:
               if (element.DataSize.Value == 8) { await WriteFloat(element.Definition, element.DoubleValue, cancellationToken); }
               else { await WriteFloat(element.Definition, element.FloatValue, cancellationToken); }
               break;
            case EBMLElementType.Date:
               await WriteDate(element.Definition, element.DateValue ?? EBMLDateElement.Epoch, cancellationToken); 
               break;
            case EBMLElementType.String:
            case EBMLElementType.UTF8:
               await WriteString(element.Definition, element.StringValue, cancellationToken);
               break;
            case EBMLElementType.Binary:
            case EBMLElementType.Unknown:
               var bin = (EBMLBinaryElement)element;
               if (bin.Reader == null) { await WriteBinary(element.Definition, bin.Value, cancellationToken); }
               else { await WriteBinary(element.Definition, new DataQueueReadStream(bin.Reader), cancellationToken); }
               break;
            case EBMLElementType.Master:
               var master = (EBMLMasterElement)element;
               await BeginMasterElement(master.Definition, bufferSize, cancellationToken);
               foreach (var child in master.Children) { await WriteElement(child, bufferSize, cancellationToken); }
               await EndMasterElement(cancellationToken);
               break;
         }
      }

      public async ValueTask BeginMasterElement(EBMLElementDefiniton def, int bufferSize = 256, CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (def == null) { throw new ArgumentNullException(nameof(def)); }
         if (def.Type != EBMLElementType.Master) { throw new ArgumentException("Element type mismatch", nameof(def)); }

         var writer = level?.Buffer ?? this.writer;
         await def.Id.Write(writer, cancellationToken);
         var newLevel = new Level(def, new DataQueue(bufferSize, cache), writer);
         if (level != null) { masterBlocks.Add(level); }
         level = newLevel;
      }

      public async ValueTask EndMasterElement(CancellationToken cancellationToken = default)
      {
         if (disposed) { throw new ObjectDisposedException(nameof(EBMLWriter)); }
         if (level == null) { throw new InvalidOperationException(); }

         var endedLevel = level;
         using (endedLevel)
         {
            if (masterBlocks.Count == 0) { level = null; }
            else
            {
               level = masterBlocks[masterBlocks.Count - 1];
               masterBlocks.RemoveAt(masterBlocks.Count - 1);
            }
            endedLevel.Buffer.CloseWrite();
            if (endedLevel.ForwardTask != null)
            {
               if (cancellationToken.CanBeCanceled && !endedLevel.ForwardTask.IsCompleted)
               {
                  await endedLevel.ForwardTask.WaitAsync(cancellationToken);
               }
               else
               {
                  await endedLevel.ForwardTask;
               }
               if (!endedLevel.Definition.AllowUnknownSize)
               {
                  if (!(stream?.CanSeek ?? false)) { throw new InvalidOperationException("Block size unknown without seekable stream"); }
                  await FlushAsync(cancellationToken);
                  await WriteSizeOnStream(endedLevel.Buffer.TotalBytesWritten, cancellationToken);
               }
            }
            else
            {
               await new EBMLVInt((ulong)endedLevel.Buffer.UnreadLength).Write(endedLevel.ParentWriter, cancellationToken);
               await endedLevel.Buffer.ForwardTo(endedLevel.ParentWriter, cancellationToken);
            }
         }
      }

      private async ValueTask WriteSizeOnStream(long blockSize, CancellationToken cancellationToken = default)
      {
         if (!(stream?.CanSeek ?? false)) { throw new InvalidOperationException("Block size unknown without seekable stream"); }
         var currentPosition = stream.Position;
         stream.Seek(-blockSize - 8, SeekOrigin.Current);
         using (var sizeBuffer = cache.Pop(8))
         {
            if ((blockSize & ~((1L << 56) - 1)) != 0) { throw new InvalidOperationException("Block size too large"); }
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(sizeBuffer.Buffer, blockSize | (1L << 56));
            await stream.WriteAsync(sizeBuffer.Buffer, 0, 8, cancellationToken);
         }
         stream.Seek(currentPosition, SeekOrigin.Begin);
      }

      public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
      {
         if (level != null)
         {
            level.ActivateStreamingMode();
            for (int i = masterBlocks.Count - 1; i >= 0; i--) { masterBlocks[i].ActivateStreamingMode(); }
            await level.Buffer.WaitForEmpty(cancellationToken);
            for (int i = masterBlocks.Count - 1; i >= 0; i--) { await masterBlocks[i].Buffer.WaitForEmpty(cancellationToken); }
         }
         if (stream != null)
         {
            await stream.FlushAsync(cancellationToken);
         }
      }

      public async ValueTask DisposeAsync()
      {
         if (disposed) { return; }
         disposed = true;
         while (level != null) { await EndMasterElement(); }
         if (!keepWriterOpen)
         {
            writer.CloseWrite();
            if (writer is IAsyncDisposable a) { await a.DisposeAsync(); }
            else if (writer is IDisposable b) { b.Dispose(); }
         }
      }

      public void Dispose()
      {
         var task = DisposeAsync();
         if (!task.IsCompletedSuccessfully) { task.AsTask().Wait(); }
      }
   }
}
