using System;
using System.IO;

namespace EBML
{
   public interface IDataQueue : IDisposable
   {
      long UnreadLength { get; }
      bool IsReadClosed { get; }
      bool IsWriteClosed { get; }
      long TotalBytesWritten { get; }
      long TotalBytesRead { get; }
      IDataQueueReader Reader { get; }
      IDataQueueWriter Writer { get; }
      Stream ReadStream { get; }
      Stream WriteStream { get; }
   }
}
