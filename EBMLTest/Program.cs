using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using MediaContainers;
using MediaContainers.Matroska;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xtremegaida.DataStructures;

namespace MediaContainersTest
{
   class Program
   {
      static void Main(string[] args)
      {
         /*var config = DefaultConfig.Instance;
         config = config.WithOptions(ConfigOptions.DisableOptimizationsValidator);
         BenchmarkRunner.Run<Test>(config);*/
         //var x = new Test();
         //for (int i = 0; i < 100; i++) { x.TestFunc().Wait(); }
         Task.Run(() => TestFunc()).Wait();
      }

      static async Task TestFunc()
      {
#if NO
         using (var buffer = new MemoryStream(File.ReadAllBytes("..\\..\\..\\test.webm")))
         //using (var buffer2 = new MemoryStream(File.ReadAllBytes("..\\..\\..\\b.webm")))
         using (var output = new MemoryStream())
         using (var doc = await MatroskaReader.Read(buffer))
         //using (var doc2 = await MatroskaReader.Read(buffer2))
         using (var docOut = await MatroskaWriter.Write(output, false, null, MatroskaWriter.DocTypeWebM))
         {
            await doc.ReadTrackInfo();
            //await doc2.ReadTrackInfo();
            //Console.WriteLine(doc.Document.Header.OriginalElement.ToFullString());
            //Console.WriteLine(doc.SeekHead.ToString());
            //Console.WriteLine(doc.Document.Body.ToFullString());

            docOut.AutoCalculateDuration = true;
            //docOut.StreamingMode = true;
            doc.Info.CopyTo(docOut.Info);
            doc.Tracks.CopyTo(docOut.Tracks);
            doc.Tags.CopyTo(docOut.Tags);
            await docOut.WriteTrackInfo();
            while (true)
            {
               var frame = await doc.ReadFrame();
               if (frame.Buffer == null) { break; }
               try { await docOut.WriteFrame(frame); }
               finally { frame.Buffer.Dispose(); }
            }
            /*var trackMap = new Dictionary<int, MatroskaTrackEntry>();
            foreach (var track in doc2.Tracks)
            {
               var found = doc.Tracks.FirstOrDefault(x => x.HasVideo == track.HasVideo && x.HasAudio == track.HasAudio);
               if (found != null) { trackMap[track.TrackNumber] = found; }
            }
            var startTimestamp = docOut.CalculatedDurationTimestamp;
            while (true)
            {
               var frame = await doc2.ReadFrame();
               if (frame.Buffer == null) { break; }
               trackMap.TryGetValue(frame.TrackIndex, out var newTrack);
               if (newTrack == null) { continue; }
               frame = new MatroskaFrame(newTrack, newTrack.TrackNumber, frame.Timestamp + startTimestamp, frame.Buffer, frame.IsKeyFrame);
               try { await docOut.WriteFrame(frame); }
               finally { frame.Buffer.Dispose(); }
            }*/
            await docOut.WriteSeekInfo();
            await docOut.DisposeAsync();

            File.WriteAllBytes("..\\..\\..\\test_full.webm", output.ToArray());

            //Console.WriteLine(doc.Document.Header.OriginalElement.ToFullString());
            //doc.Document.Reader.MaxInlineBinarySize = 256;
            //while (!doc.Document.Body.IsFullyRead) { await doc.Document.Reader.ReadNextElement(); }
         }
#endif
#if !NO
         int clusterCount = 0;
         using (var buffer = new MemoryStream(File.ReadAllBytes("..\\..\\..\\test.webm")))
         using (var doc = await MatroskaReader.Read(buffer))
         {
            await doc.ReadTrackInfo();
            Console.WriteLine(doc.Document.Header.OriginalElement.ToFullString());
            Console.WriteLine(doc.SeekHead.ToString());
            var cacheOn = false;
            while (true)
            {
               var def = (await doc.Document.Reader.ReadNextElementRaw(cacheOn)).Definition;
               if (def == null) { break; }
               if (def == MatroskaSpecification.Cluster) { clusterCount++; }
               if (!def.FullPath.StartsWith("\\Segment\\Cluster")) { cacheOn = true; }
            }
            Console.WriteLine(doc.Document.Body.ToFullString());
            Console.WriteLine("Cluster Count: " + clusterCount);
         }
         clusterCount = 0;
         using (var buffer = new MemoryStream(File.ReadAllBytes("..\\..\\..\\test_full.webm")))
         using (var doc = await MatroskaReader.Read(buffer))
         {
            await doc.ReadTrackInfo();
            Console.WriteLine(doc.Document.Header.OriginalElement.ToFullString());
            Console.WriteLine(doc.SeekHead.ToString());
            var cacheOn = false;
            while (true)
            {
               var def = (await doc.Document.Reader.ReadNextElementRaw(cacheOn)).Definition;
               if (def == null) { break; }
               if (def == MatroskaSpecification.Cluster) { clusterCount++; }
               if (!def.FullPath.StartsWith("\\Segment\\Cluster")) { cacheOn = true; }
            }
            Console.WriteLine(doc.Document.Body.ToFullString());
            Console.WriteLine("Cluster Count: " + clusterCount);
         }
#endif
      }
   }

      [MemoryDiagnoser]
   [RyuJitX64Job]
   public class Test
   {
      public static byte[] fileInput;

      static Test()
      {
         fileInput = File.ReadAllBytes(@"D:\Robert\EBML\EBMLTest\c.webm");
      }

      [Benchmark]
      public async Task TestFunc()
      {
         var cache = new DataBufferCache();
         using (var output = new MemoryStream())
         using (var doc = await MatroskaReader.Read(new DataQueueMemoryReader(fileInput), false, cache))
         using (var docOut = await MatroskaWriter.Write(output, false, null, MatroskaWriter.DocTypeWebM))
         {
            await doc.ReadTrackInfo();
            doc.Info.CopyTo(docOut.Info);
            doc.Tracks.CopyTo(docOut.Tracks);
            await docOut.WriteTrackInfo();
            while (true)
            {
               var frame = await doc.ReadFrame();
               if (frame.Buffer == null) { break; }
               try { await docOut.WriteFrame(frame); }
               finally { frame.Buffer.Dispose(); }
            }
            await docOut.WriteSeekInfo();
            await docOut.DisposeAsync();
         }
      }
   }
}
