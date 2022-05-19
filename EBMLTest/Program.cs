using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using EBML;
using EBML.Matroska;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace EBMLTest
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
         using (var buffer = new MemoryStream(File.ReadAllBytes("..\\..\\..\\c.webm")))
         using (var output = new MemoryStream())
         using (var doc = await MatroskaReader.Read(buffer))
         using (var docOut = await MatroskaWriter.Write(output, false, null, MatroskaWriter.DocTypeWebM))
         {
            await doc.ReadTrackInfo();
            Console.WriteLine(doc.Document.Header.OriginalElement.ToFullString());
            //Console.WriteLine(doc.SeekHead.ToString());
            //Console.WriteLine(doc.Document.Body.ToFullString());

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
            await docOut.WriteSeekInfo();
            await docOut.DisposeAsync();

            File.WriteAllBytes("..\\..\\..\\c2.webm", output.ToArray());

            //Console.WriteLine(doc.Document.Header.OriginalElement.ToFullString());
            //doc.Document.Reader.MaxInlineBinarySize = 256;
            //while (!doc.Document.Body.IsFullyRead) { await doc.Document.Reader.ReadNextElement(); }
         }
         int clusterCount = 0;
         using (var buffer = new MemoryStream(File.ReadAllBytes("..\\..\\..\\c2.webm")))
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
         using (var buffer = new MemoryStream(File.ReadAllBytes("..\\..\\..\\c.webm")))
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
/*
\EBML:<MASTER>
  \EBML\EBMLVersion:1
  \EBML\EBMLReadVersion:1
  \EBML\EBMLMaxIDLength:4
  \EBML\EBMLMaxSizeLength:8
  \EBML\DocType:webm
  \EBML\DocTypeVersion:4
  \EBML\DocTypeReadVersion:2

\Segment:<MASTER>
  \Segment\SeekHead:<MASTER>
    \Segment\SeekHead\Seek:<MASTER>
      \Segment\SeekHead\Seek\SeekID:<BINARY>
      \Segment\SeekHead\Seek\SeekPosition:229
    \Segment\SeekHead\Seek:<MASTER>
      \Segment\SeekHead\Seek\SeekID:<BINARY>
      \Segment\SeekHead\Seek\SeekPosition:284
    \Segment\SeekHead\Seek:<MASTER>
      \Segment\SeekHead\Seek\SeekID:<BINARY>
      \Segment\SeekHead\Seek\SeekPosition:446
    \Segment\SeekHead\Seek:<MASTER>
      \Segment\SeekHead\Seek\SeekID:<BINARY>
      \Segment\SeekHead\Seek\SeekPosition:375717
  Void:<BINARY>
  \Segment\Info:<MASTER>
    \Segment\Info\TimestampScale:1000000
    \Segment\Info\MuxingApp:Lavf58.29.100
    \Segment\Info\WritingApp:Lavf58.29.100
    \Segment\Info\Duration:6419
  \Segment\Tracks:<MASTER>
    \Segment\Tracks\TrackEntry:<MASTER>
      \Segment\Tracks\TrackEntry\TrackNumber:1
      \Segment\Tracks\TrackEntry\TrackUID:1
      \Segment\Tracks\TrackEntry\FlagLacing:0
      \Segment\Tracks\TrackEntry\Language:eng
      \Segment\Tracks\TrackEntry\CodecID:V_VP9
      \Segment\Tracks\TrackEntry\TrackType:1
      \Segment\Tracks\TrackEntry\Video:<MASTER>
        \Segment\Tracks\TrackEntry\Video\PixelWidth:960
        \Segment\Tracks\TrackEntry\Video\PixelHeight:720
        \Segment\Tracks\TrackEntry\Video\AlphaMode:1
        \Segment\Tracks\TrackEntry\Video\Colour:<MASTER>
          \Segment\Tracks\TrackEntry\Video\Colour\Range:1
    \Segment\Tracks\TrackEntry:<MASTER>
      \Segment\Tracks\TrackEntry\TrackNumber:2
      \Segment\Tracks\TrackEntry\TrackUID:2
      \Segment\Tracks\TrackEntry\FlagLacing:0
      \Segment\Tracks\TrackEntry\Language:eng
      \Segment\Tracks\TrackEntry\CodecID:A_OPUS
      \Segment\Tracks\TrackEntry\SeekPreRoll:80000000
      \Segment\Tracks\TrackEntry\TrackType:2
      \Segment\Tracks\TrackEntry\Audio:<MASTER>
        \Segment\Tracks\TrackEntry\Audio\Channels:2
        \Segment\Tracks\TrackEntry\Audio\SamplingFrequency:48000
        \Segment\Tracks\TrackEntry\Audio\BitDepth:32
      \Segment\Tracks\TrackEntry\CodecPrivate:<BINARY>
  \Segment\Tags:<MASTER>
    \Segment\Tags\Tag:<MASTER>
      \Segment\Tags\Tag\Targets:<MASTER>
      \Segment\Tags\Tag\+SimpleTag:<MASTER>
        \Segment\Tags\Tag\+SimpleTag\TagName:ENCODER
        \Segment\Tags\Tag\+SimpleTag\TagString:Lavf58.29.100
    \Segment\Tags\Tag:<MASTER>
      \Segment\Tags\Tag\Targets:<MASTER>
        \Segment\Tags\Tag\Targets\TagTrackUID:1
      \Segment\Tags\Tag\+SimpleTag:<MASTER>
        \Segment\Tags\Tag\+SimpleTag\TagName:ALPHA_MODE
        \Segment\Tags\Tag\+SimpleTag\TagString:1
    \Segment\Tags\Tag:<MASTER>
      \Segment\Tags\Tag\Targets:<MASTER>
        \Segment\Tags\Tag\Targets\TagTrackUID:1
      \Segment\Tags\Tag\+SimpleTag:<MASTER>
        \Segment\Tags\Tag\+SimpleTag\TagName:DURATION
        \Segment\Tags\Tag\+SimpleTag\TagString:00:00:06.415000000
    \Segment\Tags\Tag:<MASTER>
      \Segment\Tags\Tag\Targets:<MASTER>
        \Segment\Tags\Tag\Targets\TagTrackUID:2
      \Segment\Tags\Tag\+SimpleTag:<MASTER>
        \Segment\Tags\Tag\+SimpleTag\TagName:DURATION
        \Segment\Tags\Tag\+SimpleTag\TagString:00:00:06.419000000
  \Segment\Cluster:<MASTER>
    \Segment\Cluster\Timestamp:0
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
  \Segment\Cluster:<MASTER>
    \Segment\Cluster\Timestamp:5040
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
    \Segment\Cluster\SimpleBlock:<BINARY>
  \Segment\Cues:<MASTER>
    \Segment\Cues\CuePoint:<MASTER>
      \Segment\Cues\CuePoint\CueTime:0
      \Segment\Cues\CuePoint\CueTrackPositions:<MASTER>
        \Segment\Cues\CuePoint\CueTrackPositions\CueTrack:1
        \Segment\Cues\CuePoint\CueTrackPositions\CueClusterPosition:695
        \Segment\Cues\CuePoint\CueTrackPositions\CueRelativePosition:3
*/