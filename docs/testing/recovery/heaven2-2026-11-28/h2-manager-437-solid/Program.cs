using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Filesystem;

var process=Process.GetCurrentProcess();
var startCpu=process.TotalProcessorTime;
var timer=Stopwatch.StartNew();
var reader=new ArchiveInspector();
using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(70));
var target=Path.Combine(args[1],"Extracted");
await reader.ExtractSafelyAsync(args[0],target,args[1],cancellation.Token);
process.Refresh();
var result=new {wallMs=timer.Elapsed.TotalMilliseconds,cpuMs=(process.TotalProcessorTime-startCpu).TotalMilliseconds};
Console.WriteLine(JsonSerializer.Serialize(result));
