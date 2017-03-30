## Summary
The NTVS (Node.js Tools for Visual Studio) UWP Extension enables developers to deploy Node.js as a [Universal Windows Platform (UWP) application](https://github.com/ms-iot/node-uwp-wrapper) 
to Windows 10 IoT Core devices (e.g. Raspberry Pi), Windows 10 Desktop, and Windows 10 Mobile. 
This extension is written as a [project subtype](https://msdn.microsoft.com/en-us/library/bb166488.aspx) of [NTVS](http://aka.ms/ntvs) and extends some if its functionality like IntelliSense and debugging.
Within Node.js (UWP) applications, you have the ability to access UWP APIs as shown in the simple calendar server example below. This is done using the [uwp](https://www.npmjs.com/package/uwp) npm package which is deployed 
automatically with your project.

```javascript
var http = require('http');
var uwp = require("uwp");

uwp.projectNamespace("Windows");
var calendar = new Windows.Globalization.Calendar();

http.createServer(function (req, res) {
    res.writeHead(200, { 'Content-Type': 'text/plain' });
    var date = calendar.getDateTime();
    res.end(String(date));
}).listen(1337);

uwp.close();
```

## Try it out!
Follow the instructions [here](https://developer.microsoft.com/en-us/windows/iot/samples/helloworldnode) for the steps to install the required software and to build your first 'Hello World' Node.js server.
You can also try out the [Blinky sample](https://developer.microsoft.com/en-us/windows/iot/samples/helloblinkynode) which shows the use of UWP APIs inside a Node.js server to toggle an LED on or off.

Other samples to try out:
* [Control a servo with Johnny-Five](https://developer.microsoft.com/en-us/windows/iot/samples/j5servocontroller)
* [Control a servo with Cylon](https://developer.microsoft.com/en-us/windows/iot/samples/cylonservonode)
* [Send data from a light sensor to Azure](https://developer.microsoft.com/en-us/windows/iot/samples/azuredatauploader)


## Deployment to PC and Phone
In order to deploy an app to your PC or Phone, you must first enable Embedded Mode. See [this](https://developer.microsoft.com/en-us/windows/iot/docs/embeddedmode) page for instructions on how to do that.
You also need to turn on `Developer mode`. This can be done on the `Settings -> Update & Security -> For Developers` page.


## Intellisense for UWP API
IntelliSense is available for UWP APIs through the [ES6 Intellisense](https://github.com/Microsoft/nodejstools/wiki/ES6-IntelliSense-Preview-in-NTVS-1.1) feature in NTVS 1.2.

  ![capture](https://cloud.githubusercontent.com/assets/8389594/14468105/227e9760-0093-11e6-98c2-cc4b8dcd05e3.PNG)

To enable IntelliSense for UWP API in your project:
* Right click on your project in the Solution Explorer and select 'Open Command Prompt Here...'
* Run `npm install @types/winrt-uwp`


## Installing npm packages
Instructions on how to use the NTVS npm UI can be found [here](https://github.com/Microsoft/nodejstools/wiki/npm-Integration).
If you are installing a native package or a package with native dependencies, there are two options required to target UWP:
* `--target_arch`: The processor architecture (arm, x86, or x64) of the device the package will be running on
* `--node_uwp_dll`: Tells npm that you are building the package for use in a UWP app

These options can be entered in the 'Other npm arguments' textbox in the `Install New npm packages` dialog:

  ![capture](https://cloud.githubusercontent.com/assets/8389594/18188096/ecab2eb4-7063-11e6-932f-d6b37aa280ea.PNG)

**Notes:**
* The version of npm used by the extension is included with the [node-chakra installer](https://aka.ms/node-chakra-installer) which is bundled in the
[release](https://aka.ms/ntvsiotlatest)
* The [uwp](https://github.com/microsoft/node-uwp) package will always be included in your project automatically so there is no need to install it.
* Existing npm packages are likely to use APIs that are banned in UWP apps
(i.e. will not pass [WACK certification](https://developer.microsoft.com/en-us/windows/develop/app-certification-kit)). In these cases, support for UWP is 
required before they can build successfully. The serialport fork [here](https://github.com/ms-iot/node-serialport/tree/uwp) gives an example of how this 
can be done.


## Unsupported Node.js API
Since we are running Node in an UWP app the are a few internal modules/functions that will either work a little differently from what you're used to (i.e. with console node.exe)
or will be unsupported due to the nature of UWP applications. Anything that is not listed here is expected to work normally.

#### Child-Process
Unsupported

#### Cluster
Unsupported

#### Console 
console.* will output messages to the Visual Studio output window. The `--debug` option needs to be passed to node for this to work.
You can also use the `--use-logger` option to redirect console output to a file in the 
[local storage path](https://msdn.microsoft.com/en-us/library/windows/apps/windows.storage.applicationdata.localfolder.aspx) of the application 
(C:\Data\Users\DefaultAccount\AppData\Local\Packages\\&lt;Your Project Name&gt;_&lt;Publisher Hash String&gt;\LocalState\nodeuwp.log).

#### Debugger 
Unsupported. For debugging, the Visual Studio JavaScript debugger is used.

#### File System
Read/write permission is automatically granted for accessing files in the [local storage path](https://msdn.microsoft.com/en-us/library/windows/apps/windows.storage.applicationdata.localfolder.aspx) 
of your application. You can also read files from the application [install path](https://msdn.microsoft.com/en-us/library/windows/apps/windows.applicationmodel.package.installedlocation.aspx). 
The storage and install path can be retrieved using `fs.uwpstoragedir`, `fs.uwpstoragedirSync`, `fs.uwpinstalldir`, and `fs.uwpinstalldirSync`. 
You may also access folders associated with [app capability declarations](https://msdn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations).
The following fs functions are not supported:
* `fs.link`
* `fs.linkSync`
* `fs.readlink`
* `fs.readlinkSync`
* `fs.symlink`
* `fs.symlinkSync`
* `fs.unlink`
* `fs.unlinkSync`
* `fs.realpath`
* `fs.realpathSync`
* `fs.watch`
* `fs.watchFile`

#### OS
The following os functions are not supported:
* `os.cpus`
* `os.homedir`
* `os.tmpdir`
* `os.userInfo`

#### Process
The following process functions/properties are not supported:
* `process.connected`
* `process.cpuUsage` (bug: this should work using [ProcessCpuUsage](https://msdn.microsoft.com/en-us/library/windows/apps/windows.system.diagnostics.processcpuusage.aspx))
* `process.disconnect`
* `process.env`
* `process.getegid`
* `process.geteuid`
* `process.getgid`
* `process.getgroups`
* `process.getuid`
* `process.initgroups`
* `process.kill`
* `process.memoryUsage` (bug: this should work using [MemoryManager](https://msdn.microsoft.com/en-us/library/windows.system.memorymanager.aspx))
* `process.send`
* `process.setegid`
* `process.seteuid`
* `process.setgid`
* `process.setgroups`
* `process.setuid`
* `process.stdin`
* `process.umask`

#### Readline
Unsupported

#### REPL
Unsupported

#### TTY
Unsupported


## Building and deploying an app package (AppX)
You have the option to build and deploy your app without using the Visual Studio UI. To do this, follow the instructions below:

* Open Developer Command Prompt for VS 2015 (or 2017).
* Navigate to your project.
* Run `msbuild <Your solution name>.sln /p:configuration=release /p:platform=<arm | x86 | x64 >` (use arm for Raspberry Pi 2 or 3 and x86 for MBM or DragonBoard).
* After running the command above, you should see a new folder with the AppX in: \Your project root\AppPackages.
* Once you have created an AppX, you can use [Windows Device Portal to deploy it](https://developer.microsoft.com/en-us/windows/iot/docs/deviceportal) to your device.
* In a SSH or PowerShell window connected to your device, run `iotstartup list` to get the full package name of your app.
* Then run `iotstartup add headless <your package name>`
* Run `shutdown /r /t 0` to reboot your device. When the reboot completes, the app will be running.


## Long paths
Workarounds for the long path limit (MAX_PATH) are listed [here](https://github.com/Microsoft/nodejs-guidelines/blob/master/windows-environment.md#max_path-explanation-and-workarounds).
If you run into [this error](https://github.com/ms-iot/ntvsiot/issues/80) while deploying to IoT Core, run the commands below to remove the limit.
* `reg add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /t REG_DWORD /d 1 /v LongPathsEnabled /f`
* `shutdown /r /t 0` (this just reboots your device)


## Referencing winmd files
You can reference a C# or C++ UWP component in your Node.js UWP app by using the Visual Studio reference manager.
To do this, right click on the `references` node in your project and select `Add Reference...`. This will bring up a dialog which you can use to either:
* Choose a winmd project (within your solution) to reference in your app.
* Browse to a winmd file to reference in your app.

Once the reference is added, you can use it from your Node.js code as shown below:

Say you have a C# project that has the method shown here:

```cs
namespace MyNamespace 
{
  public class MyClass 
  {
    public string MyMethod()
    {
      return "Hello!";
    }
  }
}
```

You can use it in your Node.js app like this to print 'Hello!':

```javascript
var uwp = require('uwp');
uwp.projectNamespace('MyNamespace');

var csref = new MyNamespace.MyClass();
console.log(csref.myMethod());
```


## Creating new issues
Please follow the guidelines below when creating new issues:
* Use a descriptive title that identifies the issue to be addressed or the requested feature (e.g., "Feature F should report ABC when XYZ is used in DEF").
* Provide a detailed description of the issue or request feature.
* For bug reports, please also:
    * Describe the expected behavior and the actual behavior.
    * Provide example code or steps that reproduce the issue.
    * Specify any relevant exception messages and stack traces.
	

## Contributing
The NTVS UWP Extension code is covered by the [MIT license](http://opensource.org/licenses/MIT). There is no formal style-guide, but contributors should try to match the style of the file they are editing. 
Feel free to reach out if you need a hand in starting to tackle an issue. For new functionality, please contact us first so we can coordinate efforts (e.g. to avoid duplication of work, roadmap fit, etc.).
Since this code is under the MIT license, all contributions will be made under that license as well. Please don’t submit anything with any other licensing statements. If you want to make a contribution 
that includes code that you received under a different license, please let us know and we can figure out what to do. Please be sure that you have the right to make your contribution, including clearance 
from your employer if applicable. You must sign the [Microsoft Contributor License Agreement (CLA)](https://cla.microsoft.com/) before submitting your pull-request. The CLA only needs to be completed once 
to cover all Microsoft OSS projects.


## Debugging the Extension
As a contributor, you can use the steps below to set up your PC to build and debug the code in this repository.

**Prerequisites:** Visual Studio 2015 (or 2017) and its SDK need to be installed.
* Install [Repo root]\Common\Prerequisites\VSTestHost.msi
* Run [Repo root]\Prerequisites\EnableSkipVerification.reg
* Uninstall Node.js Tools for UWP Apps if it is installed.
* Install NTVS from [here](http://aka.ms/ntvslatest).
* Run [Repo root]\Tests\PrepUwpEnv.ps1 (as Administrator) to copy files required by the Node.js UWP project system. You may specify "-clean true" as an argument to delete existing files.
* Open [Repo root]\NodejsUwp.sln.
* Hit F5 (or Debug Menu -> Start Debugging) to build and start debugging.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). 
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) 
or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
