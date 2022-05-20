# EBML / Matroska / WebM
EBML Reader and Writer in C#.
Supports demuxing and muxing of Matroska and WebM containers.

# Example - Demux Matroska and mux WebM
```
using (var doc = await MatroskaReader.Read(new BufferedStream(File.OpenRead("input.mkv"))))
using (var docOut = await MatroskaWriter.Write(new BufferedStream(File.OpenWrite("output.webm")), false, null, MatroskaWriter.DocTypeWebM))
{
  await doc.ReadTrackInfo(); // Populate track info from input file.
  
  // Copy metadata from input to output
  doc.Info.CopyTo(docOut.Info);
  doc.Tracks.CopyTo(docOut.Tracks);
  docOut.AutoCalculateDuration = true; // Calculate duration from frame timestamps
  await docOut.WriteTrackInfo();
  
  // Copy all frames from input to output
  while (true)
  {
     var frame = await doc.ReadFrame();
     if (frame.Buffer == null) { break; }
     try { await docOut.WriteFrame(frame); }
     finally { frame.Buffer.Dispose(); }
  }
  
  // Write seek / cues and close
  await docOut.WriteSeekInfo();
  await docOut.DisposeAsync();
}
```

# Example - Stream WebM over network
```
using (var docOut = await MatroskaWriter.Write(networkStream, false, null, MatroskaWriter.DocTypeWebM))
{
  docOut.StreamingMode = true; // Disable seek / cue and write frames as they arrive
  docOut.AutoCalculateDuration = true; // Calculate duration from frame timestamps
  
  doc.Tracks.Add(track1); // etc..
  await docOut.WriteTrackInfo();
  
  // Copy all frames from input to output
  while (!done)
  {
    using (var buffer = DataBufferCache.DefaultCache.Pop(maxFrameSize))
    {
       // Populate buffer with frame data
       //..
       // "track" is one of the items added to doc.Tracks
       await docOut.WriteFrame(new MatroskaFrame(track, track.TrackNumber, timestamp, buffer, keyFrame));
    }
  }
}
```
