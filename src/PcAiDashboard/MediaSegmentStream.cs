using System.IO;
namespace PcAiDashboard;

// Presents one HTTP byte range as a seekable stream without loading a video into RAM.
public sealed class MediaSegmentStream : Stream
{
    private readonly FileStream file;
    private readonly long offset,length;
    public MediaSegmentStream(string path,long offset,long length) { file=new(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete); this.offset=offset;this.length=length;file.Position=offset; }
    public override bool CanRead=>true;
    public override bool CanSeek=>true;
    public override bool CanWrite=>false;
    public override long Length=>length;
    public override long Position { get=>file.Position-offset; set { if(value<0 || value>length) throw new ArgumentOutOfRangeException(nameof(value)); file.Position=offset+value; } }
    public override int Read(byte[] buffer,int index,int count)=>file.Read(buffer,index,(int)Math.Min(count,length-Position));
    public override int Read(Span<byte> buffer)=>file.Read(buffer[..(int)Math.Min(buffer.Length,length-Position)]);
    public override long Seek(long value,SeekOrigin origin) { Position=origin switch { SeekOrigin.Begin=>value,SeekOrigin.Current=>Position+value,SeekOrigin.End=>length+value,_=>throw new ArgumentException() };return Position; }
    public override void Flush() { }
    public override void SetLength(long value)=>throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if(disposing) file.Dispose();base.Dispose(disposing); }
}
