using HidSharp;
using System.IO.MemoryMappedFiles;
using System.Text;
foreach(var d in DeviceList.Local.GetHidDevices(0x0c70)) {
 Console.WriteLine($"{d.ProductID:X4} input={d.GetMaxInputReportLength()} feature={d.GetMaxFeatureReportLength()}");
 try { using var s=d.Open(); s.ReadTimeout=2500; var b=new byte[d.GetMaxInputReportLength()]; var n=s.Read(b); Console.WriteLine($"read={n} head={Convert.ToHexString(b.AsSpan(0,Math.Min(n,8)))}"); if(n>=327) { Console.WriteLine("temps "+string.Join(",",new[]{0x3d,0x3f,0x41,0x43}.Select(i=>(b[i]*256+b[i+1])/100.0))); Console.WriteLine("rpm "+string.Join(",",new[]{0x85,0x92,0x9f,0xac,0xb9}.Select(i=>b[i]*256+b[i+1]))); } } catch(Exception e) {Console.WriteLine(e.Message);} }
try { using var m=MemoryMappedFile.OpenExisting("PC-AI-Dashboard"); using var v=m.CreateViewStream(0,0,MemoryMappedFileAccess.Read); var b=new byte[Math.Min(v.Length,10000)]; v.ReadExactly(b); Console.WriteLine(Encoding.UTF8.GetString(b).TrimEnd('\0')); }catch(Exception e){Console.WriteLine(e.Message);}
