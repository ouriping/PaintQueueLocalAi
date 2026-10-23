using System.Text;
using PaintQueueLocalAi.Simulation;

Console.OutputEncoding = Encoding.UTF8;

var report = await new InMemoryPaintShopSimulator().RunAllAsync().ConfigureAwait(false);
Console.WriteLine(report.ToChineseText());
return report.AllPassed ? 0 : 1;
