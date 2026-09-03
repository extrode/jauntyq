# Third-Party Notices

Two JauntyQ packages redistribute third-party assemblies inside the package
itself, so that a consumer does not need a matching `PackageReference` of their
own. This file lists what each one carries and reproduces the license under
which it is distributed.

Bundling changes nothing about those licenses: each assembly remains licensed to
you by its own copyright holder under the terms reproduced below, independent of
and unaffected by the Islamic Software License - Restricted (ISL-R), Version 1.2,
and its Output Exception (ISL-OE), Version 1.2, that govern JauntyQ itself (see
`LICENSE.md`, `EXCEPTION.md` and `NOTICE.md`).

## `Extrode.JauntyQ.Generator`

Bundled into `analyzers/dotnet/cs`, because a Roslyn analyzer cannot declare
ordinary package dependencies - NuGet does not resolve them into the compiler host.

| Package | Version | License |
|---|---|---|
| System.Text.Json | 8.0.5 | MIT |
| System.Text.Encodings.Web | 8.0.0 | MIT |
| Microsoft.Bcl.AsyncInterfaces | 8.0.0 | MIT |
| System.Buffers | 4.5.1 | MIT |
| System.Memory | 4.5.5 | MIT |
| System.Runtime.CompilerServices.Unsafe | 6.0.0 | MIT |
| System.Threading.Tasks.Extensions | 4.5.4 | MIT |

## `Extrode.JauntyQ.Cli`

A .NET global tool, so `PackAsTool` bundles the whole publish output under
`tools/net8.0/any/`. Alongside the six `Extrode.JauntyQ.*` assemblies, the
package carries the database drivers and their dependency closure:

| Assembly | Package | Version | License |
|---|---|---|---|
| Azure.Core.dll | Azure.Core | 1.38.0 | MIT |
| Azure.Identity.dll | Azure.Identity | 1.11.4 | MIT |
| Microsoft.Bcl.AsyncInterfaces.dll | Microsoft.Bcl.AsyncInterfaces | 1.1.1 | MIT |
| Microsoft.Data.SqlClient.dll | Microsoft.Data.SqlClient | 5.2.2 | MIT |
| Microsoft.Data.SqlClient.resources.dll | Microsoft.Data.SqlClient | 5.2.2 | MIT |
| Microsoft.Data.SqlClient.SNI.dll | Microsoft.Data.SqlClient.SNI.runtime | 5.2.0 | Microsoft Software License Terms |
| Microsoft.Data.Sqlite.dll | Microsoft.Data.Sqlite.Core | 8.0.11 | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions.dll | Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT |
| Microsoft.Extensions.Logging.Abstractions.dll | Microsoft.Extensions.Logging.Abstractions | 8.0.2 | MIT |
| Microsoft.Identity.Client.dll | Microsoft.Identity.Client | 4.61.3 | MIT |
| Microsoft.Identity.Client.Extensions.Msal.dll | Microsoft.Identity.Client.Extensions.Msal | 4.61.3 | MIT |
| Microsoft.IdentityModel.Abstractions.dll | Microsoft.IdentityModel.Abstractions | 6.35.0 | MIT |
| Microsoft.IdentityModel.JsonWebTokens.dll | Microsoft.IdentityModel.JsonWebTokens | 6.35.0 | MIT |
| Microsoft.IdentityModel.Logging.dll | Microsoft.IdentityModel.Logging | 6.35.0 | MIT |
| Microsoft.IdentityModel.Protocols.dll | Microsoft.IdentityModel.Protocols | 6.35.0 | MIT |
| Microsoft.IdentityModel.Protocols.OpenIdConnect.dll | Microsoft.IdentityModel.Protocols.OpenIdConnect | 6.35.0 | MIT |
| Microsoft.IdentityModel.Tokens.dll | Microsoft.IdentityModel.Tokens | 6.35.0 | MIT |
| Microsoft.SqlServer.Server.dll | Microsoft.SqlServer.Server | 1.0.0 | MIT |
| MySqlConnector.dll | MySqlConnector | 2.4.0 | MIT |
| Npgsql.dll | Npgsql | 8.0.6 | PostgreSQL |
| SQLitePCLRaw.batteries_v2.dll | SQLitePCLRaw.bundle_e_sqlite3 | 3.0.3 | Apache-2.0 |
| SQLitePCLRaw.core.dll | SQLitePCLRaw.core | 3.0.3 | Apache-2.0 |
| SQLitePCLRaw.provider.e_sqlite3.dll | SQLitePCLRaw.provider.e_sqlite3 | 3.0.3 | Apache-2.0 |
| System.ClientModel.dll | System.ClientModel | 1.0.0 | MIT |
| System.Configuration.ConfigurationManager.dll | System.Configuration.ConfigurationManager | 8.0.0 | MIT |
| System.Diagnostics.EventLog.dll | System.Diagnostics.EventLog | 8.0.0 | MIT |
| System.Diagnostics.EventLog.Messages.dll | System.Diagnostics.EventLog | 8.0.0 | MIT |
| System.IdentityModel.Tokens.Jwt.dll | System.IdentityModel.Tokens.Jwt | 6.35.0 | MIT |
| System.Memory.Data.dll | System.Memory.Data | 1.0.2 | MIT |
| System.Runtime.Caching.dll | System.Runtime.Caching | 8.0.0 | MIT |
| System.Security.Cryptography.ProtectedData.dll | System.Security.Cryptography.ProtectedData | 8.0.0 | MIT |
| e_sqlite3.dll | SourceGear.sqlite3 | 3.50.4.5 | Public domain (SQLite) |

`Microsoft.Data.SqlClient.SNI.dll` is the one entry above that is not under an
OSI-approved license. Microsoft permits redistributing it in object form as part
of an application, subject to the conditions in section 3(a) of the terms
reproduced below - including a requirement that distributors and end users agree
to terms protecting it at least as much as those terms do.

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

## PostgreSQL License

Applies to `Npgsql`.

```
PostgreSQL Database Management System
(formerly known as Postgres, then as Postgres95)

Portions Copyright (c) 1996-2010, The PostgreSQL Global Development Group

Portions Copyright (c) 1994, The Regents of the University of California

Permission to use, copy, modify, and distribute this software and its documentation for any purpose, without fee, and without a written agreement is hereby granted, provided that the above copyright notice and this paragraph and the following two paragraphs appear in all copies.

IN NO EVENT SHALL THE UNIVERSITY OF CALIFORNIA BE LIABLE TO ANY PARTY FOR DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES, INCLUDING LOST PROFITS, ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS DOCUMENTATION, EVEN IF THE UNIVERSITY OF CALIFORNIA HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

THE UNIVERSITY OF CALIFORNIA SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE. THE SOFTWARE PROVIDED HEREUNDER IS ON AN "AS IS" BASIS, AND THE UNIVERSITY OF CALIFORNIA HAS NO OBLIGATIONS TO PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
```

## Apache License 2.0

Applies to the `SQLitePCLRaw` assemblies.

```
Apache License
Version 2.0, January 2004
http://www.apache.org/licenses/

TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

1. Definitions.

"License" shall mean the terms and conditions for use, reproduction, and distribution as defined by Sections 1 through 9 of this document.

"Licensor" shall mean the copyright owner or entity authorized by the copyright owner that is granting the License.

"Legal Entity" shall mean the union of the acting entity and all other entities that control, are controlled by, or are under common control with that entity. For the purposes of this definition, "control" means (i) the power, direct or indirect, to cause the direction or management of such entity, whether by contract or otherwise, or (ii) ownership of fifty percent (50%) or more of the outstanding shares, or (iii) beneficial ownership of such entity.

"You" (or "Your") shall mean an individual or Legal Entity exercising permissions granted by this License.

"Source" form shall mean the preferred form for making modifications, including but not limited to software source code, documentation source, and configuration files.

"Object" form shall mean any form resulting from mechanical transformation or translation of a Source form, including but not limited to compiled object code, generated documentation, and conversions to other media types.

"Work" shall mean the work of authorship, whether in Source or Object form, made available under the License, as indicated by a copyright notice that is included in or attached to the work (an example is provided in the Appendix below).

"Derivative Works" shall mean any work, whether in Source or Object form, that is based on (or derived from) the Work and for which the editorial revisions, annotations, elaborations, or other modifications represent, as a whole, an original work of authorship. For the purposes of this License, Derivative Works shall not include works that remain separable from, or merely link (or bind by name) to the interfaces of, the Work and Derivative Works thereof.

"Contribution" shall mean any work of authorship, including the original version of the Work and any modifications or additions to that Work or Derivative Works thereof, that is intentionally submitted to Licensor for inclusion in the Work by the copyright owner or by an individual or Legal Entity authorized to submit on behalf of the copyright owner. For the purposes of this definition, "submitted" means any form of electronic, verbal, or written communication sent to the Licensor or its representatives, including but not limited to communication on electronic mailing lists, source code control systems, and issue tracking systems that are managed by, or on behalf of, the Licensor for the purpose of discussing and improving the Work, but excluding communication that is conspicuously marked or otherwise designated in writing by the copyright owner as "Not a Contribution."

"Contributor" shall mean Licensor and any individual or Legal Entity on behalf of whom a Contribution has been received by Licensor and subsequently incorporated within the Work.

2. Grant of Copyright License. Subject to the terms and conditions of this License, each Contributor hereby grants to You a perpetual, worldwide, non-exclusive, no-charge, royalty-free, irrevocable copyright license to reproduce, prepare Derivative Works of, publicly display, publicly perform, sublicense, and distribute the Work and such Derivative Works in Source or Object form.

3. Grant of Patent License. Subject to the terms and conditions of this License, each Contributor hereby grants to You a perpetual, worldwide, non-exclusive, no-charge, royalty-free, irrevocable (except as stated in this section) patent license to make, have made, use, offer to sell, sell, import, and otherwise transfer the Work, where such license applies only to those patent claims licensable by such Contributor that are necessarily infringed by their Contribution(s) alone or by combination of their Contribution(s) with the Work to which such Contribution(s) was submitted. If You institute patent litigation against any entity (including a cross-claim or counterclaim in a lawsuit) alleging that the Work or a Contribution incorporated within the Work constitutes direct or contributory patent infringement, then any patent licenses granted to You under this License for that Work shall terminate as of the date such litigation is filed.

4. Redistribution. You may reproduce and distribute copies of the Work or Derivative Works thereof in any medium, with or without modifications, and in Source or Object form, provided that You meet the following conditions:

     (a) You must give any other recipients of the Work or Derivative Works a copy of this License; and

     (b) You must cause any modified files to carry prominent notices stating that You changed the files; and

     (c) You must retain, in the Source form of any Derivative Works that You distribute, all copyright, patent, trademark, and attribution notices from the Source form of the Work, excluding those notices that do not pertain to any part of the Derivative Works; and

     (d) If the Work includes a "NOTICE" text file as part of its distribution, then any Derivative Works that You distribute must include a readable copy of the attribution notices contained within such NOTICE file, excluding those notices that do not pertain to any part of the Derivative Works, in at least one of the following places: within a NOTICE text file distributed as part of the Derivative Works; within the Source form or documentation, if provided along with the Derivative Works; or, within a display generated by the Derivative Works, if and wherever such third-party notices normally appear. The contents of the NOTICE file are for informational purposes only and do not modify the License. You may add Your own attribution notices within Derivative Works that You distribute, alongside or as an addendum to the NOTICE text from the Work, provided that such additional attribution notices cannot be construed as modifying the License.

     You may add Your own copyright statement to Your modifications and may provide additional or different license terms and conditions for use, reproduction, or distribution of Your modifications, or for any such Derivative Works as a whole, provided Your use, reproduction, and distribution of the Work otherwise complies with the conditions stated in this License.

5. Submission of Contributions. Unless You explicitly state otherwise, any Contribution intentionally submitted for inclusion in the Work by You to the Licensor shall be under the terms and conditions of this License, without any additional terms or conditions. Notwithstanding the above, nothing herein shall supersede or modify the terms of any separate license agreement you may have executed with Licensor regarding such Contributions.

6. Trademarks. This License does not grant permission to use the trade names, trademarks, service marks, or product names of the Licensor, except as required for reasonable and customary use in describing the origin of the Work and reproducing the content of the NOTICE file.

7. Disclaimer of Warranty. Unless required by applicable law or agreed to in writing, Licensor provides the Work (and each Contributor provides its Contributions) on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied, including, without limitation, any warranties or conditions of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A PARTICULAR PURPOSE. You are solely responsible for determining the appropriateness of using or redistributing the Work and assume any risks associated with Your exercise of permissions under this License.

8. Limitation of Liability. In no event and under no legal theory, whether in tort (including negligence), contract, or otherwise, unless required by applicable law (such as deliberate and grossly negligent acts) or agreed to in writing, shall any Contributor be liable to You for damages, including any direct, indirect, special, incidental, or consequential damages of any character arising as a result of this License or out of the use or inability to use the Work (including but not limited to damages for loss of goodwill, work stoppage, computer failure or malfunction, or any and all other commercial damages or losses), even if such Contributor has been advised of the possibility of such damages.

9. Accepting Warranty or Additional Liability. While redistributing the Work or Derivative Works thereof, You may choose to offer, and charge a fee for, acceptance of support, warranty, indemnity, or other liability obligations and/or rights consistent with this License. However, in accepting such obligations, You may act only on Your own behalf and on Your sole responsibility, not on behalf of any other Contributor, and only if You agree to indemnify, defend, and hold each Contributor harmless for any liability incurred by, or claims asserted against, such Contributor by reason of your accepting any such warranty or additional liability.

END OF TERMS AND CONDITIONS

APPENDIX: How to apply the Apache License to your work.

To apply the Apache License to your work, attach the following boilerplate notice, with the fields enclosed by brackets "[]" replaced with your own identifying information. (Don't include the brackets!)  The text should be enclosed in the appropriate comment syntax for the file format. We also recommend that a file or class name and description of purpose be included on the same "printed page" as the copyright notice for easier identification within third-party archives.

Copyright [yyyy] [name of copyright owner]

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

## SQLite

`e_sqlite3.dll` is a build of SQLite, which is in the public domain
(https://sqlite.org/copyright.html), packaged by SourceGear, LLC as
`SourceGear.sqlite3`.

## Microsoft Software License Terms - MICROSOFT.DATA.SQLCLIENT.SNI LIBRARY

Applies to `Microsoft.Data.SqlClient.SNI.dll`.

```
MICROSOFT SOFTWARE LICENSE TERMS

MICROSOFT.DATA.SQLCLIENT.SNI LIBRARY

These license terms are an agreement between you and Microsoft Corporation (or based on where you live, one of its affiliates). They apply to the software named above. The terms also apply to any Microsoft services or updates for the software, except to the extent those have different terms.

IF YOU COMPLY WITH THESE LICENSE TERMS, YOU HAVE THE RIGHTS BELOW.

1.  INSTALLATION AND USE RIGHTS.
    You may install and use any number of copies of the software to develop and test your applications. 
2.  THIRD PARTY COMPONENTS. The software may include third party components with separate legal notices or governed by other agreements, as may be described in the ThirdPartyNotices file(s) accompanying the software.
3.  ADDITIONAL LICENSING REQUIREMENTS AND/OR USE RIGHTS.
    a. DISTRIBUTABLE CODE.  The software is comprised of Distributable Code. "Distributable Code" is code that you are permitted to distribute in applications you develop if you comply with the terms below.
       i. Right to Use and Distribute.
          * You may copy and distribute the object code form of the software.
          * Third Party Distribution. You may permit distributors of your applications to copy and distribute the Distributable Code as part of those applications.
      ii. Distribution Requirements. For any Distributable Code you distribute, you must
          * use the Distributable Code in your applications and not as a standalone distribution;
          * require distributors and external end users to agree to terms that protect it at least as much as this agreement; and
          * indemnify, defend, and hold harmless Microsoft from any claims, including attorneys' fees, related to the distribution or use of your applications, except to the extent that any claim is based solely on the unmodified Distributable Code.
     iii. Distribution Restrictions. You may not
          * use Microsoft's trademarks in your applications' names or in a way that suggests your applications come from or are endorsed by Microsoft; or
          * modify or distribute the source code of any Distributable Code so that any part of it becomes subject to an Excluded License. An "Excluded License" is one that requires, as a condition of use, modification or distribution of code, that (i) it be disclosed or distributed in source code form; or (ii) others have the right to modify it.
4.  DATA.
    a. Data Collection. Some features in the software may enable collection of data from users of your applications that access or use the software. If you use these features to enable data collection in your applications, you must comply with applicable law, including getting any required user consent, and maintain a prominent privacy policy that accurately informs users about how you use, collect, and share their data. You agree to comply with all applicable provisions of the Microsoft Privacy Statement at [https://go.microsoft.com/fwlink/?LinkId=521839].
5.  SCOPE OF LICENSE. The software is licensed, not sold. This agreement only gives you some rights to use the software. Microsoft reserves all other rights. Unless applicable law gives you more rights despite this limitation, you may use the software only as expressly permitted in this agreement. In doing so, you must comply with any technical limitations in the software that only allow you to use it in certain ways. You may not
    * work around any technical limitations in the software;
    * reverse engineer, decompile or disassemble the software, or otherwise attempt to derive the source code for the software, except and to the extent required by third party licensing terms governing use of certain open source components that may be included in the software;
    * remove, minimize, block or modify any notices of Microsoft or its suppliers in the software;
    * use the software in any way that is against the law; or
    * share, publish, rent or lease the software, provide the software as a stand-alone offering for others to use, or transfer the software or this agreement to any third party.
6.  EXPORT RESTRICTIONS. You must comply with all domestic and international export laws and regulations that apply to the software, which include restrictions on destinations, end users, and end use. For further information on export restrictions, visit www.microsoft.com/exporting.  
7.  SUPPORT SERVICES. Because this software is "as is," we may not provide support services for it.
8.  ENTIRE AGREEMENT. This agreement, and the terms for supplements, updates, Internet-based services and support services that you use, are the entire agreement for the software and support services.
9.  APPLICABLE LAW.  If you acquired the software in the United States, Washington law applies to interpretation of and claims for breach of this agreement, and the laws of the state where you live apply to all other claims. If you acquired the software in any other country, its laws apply.
10. CONSUMER RIGHTS; REGIONAL VARIATIONS. This agreement describes certain legal rights. You may have other rights, including consumer rights, under the laws of your state or country. Separate and apart from your relationship with Microsoft, you may also have rights with respect to the party from which you acquired the software. This agreement does not change those other rights if the laws of your state or country do not permit it to do so. For example, if you acquired the software in one of the below regions, or mandatory country law applies, then the following provisions apply to you:
    a) Australia. You have statutory guarantees under the Australian Consumer Law and nothing in this agreement is intended to affect those rights.
    b) Canada. If you acquired this software in Canada, you may stop receiving updates by turning off the automatic update feature, disconnecting your device from the Internet (if and when you re-connect to the Internet, however, the software will resume checking for and installing updates), or uninstalling the software. The product documentation, if any, may also specify how to turn off updates for your specific device or software.
    c) Germany and Austria.
       (i) Warranty. The software will perform substantially as described in any Microsoft materials that accompany it. However, Microsoft gives no contractual guarantee in relation to the software.
       (ii) Limitation of Liability. In case of intentional conduct, gross negligence, claims based on the Product Liability Act, as well as in case of death or personal or physical injury, Microsoft is liable according to the statutory law.
    Subject to the foregoing clause (ii), Microsoft will only be liable for slight negligence if Microsoft is in breach of such material contractual obligations, the fulfillment of which facilitate the due performance of this agreement, the breach of which would endanger the purpose of this agreement and the compliance with which a party may constantly trust in (so-called "cardinal obligations"). In other cases of slight negligence, Microsoft will not be liable for slight negligence
11. DISCLAIMER OF WARRANTY. THE SOFTWARE IS LICENSED "AS-IS." YOU BEAR THE RISK OF USING IT. MICROSOFT GIVES NO EXPRESS WARRANTIES, GUARANTEES OR CONDITIONS. TO THE EXTENT PERMITTED UNDER YOUR LOCAL LAWS, MICROSOFT EXCLUDES THE IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NON-INFRINGEMENT.
12. LIMITATION ON AND EXCLUSION OF REMEDIES AND DAMAGES. YOU CAN RECOVER FROM MICROSOFT AND ITS SUPPLIERS ONLY DIRECT DAMAGES UP TO U.S. $5.00. YOU CANNOT RECOVER ANY OTHER DAMAGES, INCLUDING CONSEQUENTIAL, LOST PROFITS, SPECIAL, INDIRECT OR INCIDENTAL DAMAGES.
    This limitation applies to (a) anything related to the software, services, content (including code) on third party Internet sites, or third party applications; and (b) claims for breach of contract, breach of warranty, guarantee or condition, strict liability, negligence, or other tort to the extent permitted by applicable law.
    It also applies even if Microsoft knew or should have known about the possibility of the damages. The above limitation or exclusion may not apply to you because your state or country may not allow the exclusion or limitation of incidental, consequential or other damages.
```
