/*
    Copyright(c) Microsoft Open Technologies, Inc. All rights reserved.

    The MIT License(MIT)

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files(the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions :

    The above copyright notice and this permission notice shall be included in
    all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    THE SOFTWARE.
*/

using System;
using System.Linq;
using System.Windows;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestUtilities;
using TestUtilities.UI;
using TestUtilities.NodejsUwp;
using Key = System.Windows.Input.Key;


namespace NodejsUwp.Tests {
    [TestClass]
    public sealed class BasicIntellisense : NodejsUwpProjectTest {

        [TestMethod, Priority(0), TestCategory("Core")]
        [HostType("VSTestHost")]
        public void CtrlSpace() {
            var project = Project("CtrlSpace",
                Compile("server", "var http = require('http');\r\nhttp.createS")
            );
           
            using (var solution = project.Generate().ToVs()) {
                var server = solution.OpenItem("CtrlSpace", "server.js");

                server.MoveCaret(2, 13);

                solution.App.Dte.ExecuteCommand("Edit.CompleteWord");

                server.WaitForText("var http = require('http');\r\nhttp.createServer");
            }
        }

        [TestMethod, Priority(0), TestCategory("Core")]
        [HostType("VSTestHost")]
        public void SignaturesTest() {
            var project = Project("SignaturesTest",
                Compile("server", "function f(a, b, c) { }\r\n\r\n")
            );

            using (var solution = project.Generate().ToVs()) {
                var server = solution.OpenItem("SignaturesTest", "server.js");

                server.MoveCaret(3, 1);

                Keyboard.Type("f(");
                
                using (var sh = server.WaitForSession<ISignatureHelpSession>()) {
                    var session = sh.Session;
                    Assert.AreEqual("a", session.SelectedSignature.CurrentParameter.Name);
                }

                Keyboard.Backspace();
                Keyboard.Backspace();

                Keyboard.Type("new f(");

                using (var sh = server.WaitForSession<ISignatureHelpSession>()) {
                    var session = sh.Session;
                    Assert.AreEqual("a", session.SelectedSignature.CurrentParameter.Name);
                }

            }
        }

        [TestMethod, Priority(0), TestCategory("Core")]
        [HostType("VSTestHost")]
        public void IntellisenseAfterNewLine() {
            var project = Project("NewLineTest",
                Compile("server", "var x = 'abc';\r\n// foo\r\nx\r\nvar abc=42\r\nx")
            );

            using (var solution = project.Generate().ToVs()) {
                var server = solution.OpenItem("NewLineTest", "server.js");

                server.MoveCaret(3, 2);

                Keyboard.Type(".");

                using (var sh = server.WaitForSession<ICompletionSession>()) {
                    var session = sh.Session;
                    AssertUtil.ContainsAtLeast(session.CompletionSets[0].Completions.Select(x => x.InsertionText), "big");
                }

                server.MoveCaret(5, 2);

                Keyboard.Type(".");

                using (var sh = server.WaitForSession<ICompletionSession>()) {
                    var session = sh.Session;
                    AssertUtil.ContainsAtLeast(session.CompletionSets[0].Completions.Select(x => x.InsertionText), "big");
                }
            }
        }
    }
}
