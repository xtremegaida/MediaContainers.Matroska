using EBML;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EBMLUnitTests
{
   [TestClass]
   public class DataBufferCacheTest
   {
      [TestMethod]
      public void AllocBuffer()
      {
         var cache = new DataBufferCache();
         var buffer = cache.Pop(500);
         Assert.IsTrue(buffer.Buffer.Length >= 500);
         Assert.AreEqual(500, cache.BufferMemoryUsedBytes);
      }

      [TestMethod]
      public void FreeBuffer()
      {
         var cache = new DataBufferCache();
         var buffer = cache.Pop(500);
         var size = buffer.Buffer.Length;
         buffer.Dispose();
         Assert.AreEqual(size, cache.BufferMemoryReserveBytes);
         Assert.AreEqual(0, cache.BufferMemoryUsedBytes);
      }

      [TestMethod]
      public void RecycleBuffer()
      {
         var cache = new DataBufferCache();
         var size = 0;
         using (var x = cache.Pop(100))
         using (var y = cache.Pop(1000))
         using (var z = cache.Pop(1500))
         {
            size = x.Buffer.Length;
            size += y.Buffer.Length;
            size += z.Buffer.Length;
            Assert.AreEqual(0, cache.BufferMemoryReserveBytes);
            Assert.AreEqual(size, cache.BufferMemoryUsedBytes);
         }
         Assert.AreEqual(size, cache.BufferMemoryReserveBytes);
         Assert.AreEqual(0, cache.BufferMemoryUsedBytes);
         using (var x = cache.Pop(100))
         using (var y = cache.Pop(1000))
         using (var z = cache.Pop(1500))
         {
            size = x.Buffer.Length;
            size += y.Buffer.Length;
            size += z.Buffer.Length;
            Assert.IsTrue(cache.BufferMemoryReserveBytes < size);
            Assert.AreEqual(size, cache.BufferMemoryUsedBytes);
         }
      }

      [TestMethod]
      public void ExtraBufferReferences()
      {
         var cache = new DataBufferCache();
         var buffer = cache.Pop(500);
         var size = buffer.Buffer.Length;
         Assert.AreEqual(0, cache.BufferMemoryReserveBytes);
         Assert.AreEqual(size, cache.BufferMemoryUsedBytes);
         buffer.AddReference();
         buffer.Dispose();
         Assert.AreEqual(0, cache.BufferMemoryReserveBytes);
         Assert.AreEqual(size, cache.BufferMemoryUsedBytes);
         buffer.Dispose();
         Assert.AreEqual(size, cache.BufferMemoryReserveBytes);
         Assert.AreEqual(0, cache.BufferMemoryUsedBytes);
      }
   }
}
