# zstd for unity

Copyright (c) 2025 NicoIer

zstd version: 1.5.7


https://github.com/facebook/zstd

ClangSharpPInvokeGenerator is used to generate the C# bindings for zstd.

https://github.com/dotnet/ClangSharp


除了基础的压缩，解压缩功能外，将zstdcli中的差分压缩和解压缩功能进行了集成。


原始的libzstd没有支持差分功能，zstdcli作为命令行工具支持了差分压缩和解压缩功能。

我想让把cli的差分功能集成到libzstd中，以便在unity中使用。

所以我对zstdcli做了一些修改，把差分压缩和解压缩的功能提取出来，集成到了libzstd中。


