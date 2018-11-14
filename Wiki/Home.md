## Project Description
The Base Class Libraries site hosts samples, previews, and prototypes from the BCL team.
This is a site for the BCL Team to get features to customers to try out without requiring a Beta or CTP of the .NET Framework.  Our goal is to put generally useful functionality here, and to get feedback on it and have the chance to iterate on the design.  Having a feature here does **not** mean that it will eventually end up in the BCL.  Some items are samples that build on top of existing classes, and some features might be ones we were considering for the .NET Framework but decide not to include for one reason or another.

We'd love to get your feedback in the form of comments, bug reports, and feature requests, but please note that we cannot take code submissions.  We plan to release updates with new features and updates to existing features on a quarterly basis, but we're just getting started with this and may adjust down the line depending on how things go.

And just to avoid confusion, you will **not** find the source code for the whole BCL on this CodePlex site.  This site is for things we're thinking about for the future, not our existing classes.

## Feature Descriptions
Below are descriptions of features currently in this project.  You can find more details about each in the [Documentation](Documentation) section of this project.

### [BigRational](BigRational.md)

BigRational builds on the [BigInteger](http://msdn.microsoft.com/en-us/library/system.numerics.biginteger(VS.100).aspx) introduced in .NET Framework 4 to create an arbitrary-precision rational number class.

### [Long Path](Long-Path.md)
This library provides functionality to make it easier to work with paths that are longer than the current 259 character limit.

### [PerfMonitor](PerfMonitor.md)
PerfMonitor is a command-line tool for profiling the system using Event Tracing for Windows (ETW).  PerfMonitor is built on top of the [TraceEvent](TraceEvent) library.

### [TraceEvent](TraceEvent.md)
An library that greatly simplifies reading Event Tracing for Windows (ETW) events.

## Related Sites
| [BCL Page on MSDN](http://msdn.microsoft.com/en-us/netframework/aa569603.aspx) |
| [BCL Team Blog](http://blogs.msdn.com/bclteam/) |
| [CLR Team Blog](http://blogs.msdn.com/clrteam/) |
