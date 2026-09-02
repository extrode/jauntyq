# Third-Party Notices

`JauntyQ.Generator` bundles the following third-party assemblies directly into its
NuGet package (`analyzers/dotnet/cs`), so that a project referencing `JauntyQ.Generator`
does not need its own matching `PackageReference` for the Roslyn analyzer to load. This
file lists them and reproduces the MIT License under which each is distributed, per that
license's requirement that the copyright and permission notice accompany redistributed
copies.

Bundling these does not change their license: each remains licensed to you by its
respective copyright holder under the MIT License below, independent of and unaffected by
the Islamic Software License - Restricted (ISL-R), Version 1.2, and its Output Exception
(ISL-OE), Version 1.2, that govern `JauntyQ.Generator` itself (see `LICENSE.md`,
`EXCEPTION.md` and `NOTICE.md`).

## Bundled packages

| Package | Version | Publisher |
|---|---|---|
| System.Text.Json | 8.0.5 | Microsoft |
| System.Text.Encodings.Web | 8.0.0 | Microsoft |
| Microsoft.Bcl.AsyncInterfaces | 8.0.0 | Microsoft |
| System.Buffers | 4.5.1 | Microsoft |
| System.Memory | 4.5.5 | Microsoft |
| System.Runtime.CompilerServices.Unsafe | 6.0.0 | Microsoft |
| System.Threading.Tasks.Extensions | 4.5.4 | Microsoft |

## MIT License

```
The MIT License (MIT)

Copyright (c) Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
