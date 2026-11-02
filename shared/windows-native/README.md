# Native window helpers

Project-authored source extracted with Panel Tools under the repository MIT license. These helpers are also used independently in a separate workspace; this repository does not require or import that project.

Keep C# 5 compatibility for the optional .NET Framework prototype alongside .NET 8. Preserve conservative canvas classification, process identity validation and hook cleanup.

Use `NET8_0_OR_GREATER` guards for nullable-analysis attributes that are unavailable in .NET Framework. Keep nullable checks enabled in the unified project; do not suppress warnings for the shared source. The prototype build enforces C# 5 and treats warnings as errors.
