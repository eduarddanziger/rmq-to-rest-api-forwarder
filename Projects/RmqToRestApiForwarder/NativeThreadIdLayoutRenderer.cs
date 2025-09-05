using System.Runtime.InteropServices;
using System.Text;
using NLog;
using NLog.LayoutRenderers;

namespace RmqToRestApiForwarder;


// Custom NLog layout renderer to get native thread ID
[LayoutRenderer("native-thread-id")]
internal partial class NativeThreadIdLayoutRenderer : LayoutRenderer
{
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        builder.Append(GetCurrentThreadId());
    }
}