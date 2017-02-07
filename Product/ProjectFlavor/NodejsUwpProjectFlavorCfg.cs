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

using Microsoft.SmartDevice.Connectivity;
using Microsoft.SmartDevice.Connectivity.Wrapper;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.NodejsUwp
{
    class NodejsUwpProjectFlavorCfg :
        IVsCfg,
        IVsProjectCfg,
        IVsProjectCfg2,
        IVsProjectFlavorCfg,
        IVsDebuggableProjectCfg,
        ISpecifyPropertyPages,
        IVsSpecifyProjectDesignerPages,
        IVsCfgBrowseObject,
        IVsDeployableProjectCfg,
        IVsProjectCfgDebugTargetSelection,
        IVsAppContainerProjectDeployCallback,
        IVsBuildPropertyStorage,
        IVsQueryDebuggableProjectCfg,
        IPersistXMLFragment,
        IVsAppContainerBootstrapperLogger
    {
        // The IVsCfg object of the base project.
        private IVsCfg _baseCfg;

        // The IVsProjectFlavorCfg object of the inner project subtype. 
        private IVsProjectFlavorCfg _innerCfg;

        private readonly object syncObject = new object();
        private EventSinkCollection deployCallbackCollection = new EventSinkCollection();
        private IVsAppContainerProjectDeployOperation appContainerDeployOperation;
        private IVsTask appContainerBootstrapperOperation;
        private IVsOutputWindowPane outputWindow;
        private string deployPackageMoniker;
        private string deployAppUserModelID;
        private IVsHierarchy project;
        private const string RemoteTarget = "Remote Machine";
        private const string LocalTarget = "Local Machine";
        private const string PhoneTarget = "Device";
        private static readonly Guid NativePortSupplier = new Guid("3b476d38-a401-11d2-aad4-00c04f990171"); // NativePortSupplier corespond to connection with No-Authentication mode
        private bool isDirty;
        private Dictionary<string, string> propertiesList = new Dictionary<string, string>();
        static Dictionary<IVsCfg, NodejsUwpProjectFlavorCfg> mapIVsCfgToNodejsUwpProjectFlavorCfg = new Dictionary<IVsCfg, NodejsUwpProjectFlavorCfg>();
        private readonly System.IServiceProvider serviceProvider;
        private IVsDebuggerDeployConnection debuggerDeployConnection;
        private enum TargetType { Remote, Local, Phone }
        private TargetType ActiveTargetType;
        private const string DeviceIdString = "30f105c9-681e-420b-a277-7c086ead8a4e"; // ID for Windows Phone device
        private readonly VsBootstrapperPackageInfo[] packagesToDeployList;
        private readonly VsBootstrapperPackageInfo[] optionalPackagesToDeploy;

        #region properties

        internal string DeployPackageMoniker
        {
            get { return this.deployPackageMoniker; }
        }

        internal string DeployAppUserModelID
        {
            get { return this.deployAppUserModelID; }
        }

        #endregion
        public NodejsUwpProjectFlavorCfg(NodejsUwpProjectFlavor baseProjectNode, IVsCfg baseConfiguration, IVsProjectFlavorCfg innerConfiguration)
        {
            _baseCfg = baseConfiguration;
            _innerCfg = innerConfiguration;
            project = baseProjectNode;
            mapIVsCfgToNodejsUwpProjectFlavorCfg.Add(baseConfiguration, this);
            serviceProvider = this.project as System.IServiceProvider;

            packagesToDeployList = new VsBootstrapperPackageInfo[] {
                new VsBootstrapperPackageInfo { PackageName = "D8B19935-BDBF-4D5B-9619-A6693AFD4554" }, // ScriptMsVsMon
                new VsBootstrapperPackageInfo { PackageName = "EB22551A-7F66-465F-B53F-E5ABA0C0574E" }, // NativeMsVsMon  
                new VsBootstrapperPackageInfo { PackageName = "62B807E2-6539-46FB-8D67-A73DC9499940" } // ManagedMsVsMon  
            };
            optionalPackagesToDeploy = new VsBootstrapperPackageInfo[] {
                new VsBootstrapperPackageInfo { PackageName = "B968CC6A-D2C8-4197-88E3-11662042C291" }, // XamlUIDebugging  
                new VsBootstrapperPackageInfo { PackageName = "8CDEABEF-33E1-4A23-A13F-94A49FF36E84" }  // XamlUIDebuggingDependency  
            };
        }

        internal IVsHierarchy NodeConfig
        {
            get
            {
                IVsHierarchy proj = null;
                var browseObj = _baseCfg as IVsCfgBrowseObject;

                if (browseObj != null)
                {
                    uint itemId = 0;
                    browseObj.GetProjectItem(out proj, out itemId);
                }
                return proj;
            }
        }

        #region IVsCfg Members

        public int get_DisplayName(out string pbstrDisplayName)
        {
            int ret = _baseCfg.get_DisplayName(out pbstrDisplayName);

            return ret;
        }

        public int get_IsDebugOnly(out int pfIsDebugOnly)
        {
            return _baseCfg.get_IsDebugOnly(out pfIsDebugOnly);
        }

        public int get_IsReleaseOnly(out int pfIsReleaseOnly)
        {
            return _baseCfg.get_IsReleaseOnly(out pfIsReleaseOnly);
        }

        #endregion

        #region IVsProjectCfg Members

        public int EnumOutputs(out IVsEnumOutputs ppIVsEnumOutputs)
        {
            IVsProjectCfg projCfg = _innerCfg as IVsProjectCfg;
            if (projCfg != null)
            {
                return projCfg.EnumOutputs(out ppIVsEnumOutputs);
            }
            ppIVsEnumOutputs = null;
            return VSConstants.E_NOTIMPL;
        }

        public int OpenOutput(string szOutputCanonicalName, out IVsOutput ppIVsOutput)
        {
            IVsProjectCfg projCfg = _innerCfg as IVsProjectCfg;
            if (projCfg != null)
            {
                return projCfg.OpenOutput(szOutputCanonicalName, out ppIVsOutput);
            }
            ppIVsOutput = null;
            return VSConstants.E_NOTIMPL;
        }

        public int get_BuildableProjectCfg(out IVsBuildableProjectCfg ppIVsBuildableProjectCfg)
        {
            IVsProjectCfg projCfg = _innerCfg as IVsProjectCfg;
            if (projCfg != null)
            {
                return projCfg.get_BuildableProjectCfg(out ppIVsBuildableProjectCfg);
            }
            ppIVsBuildableProjectCfg = null;
            return VSConstants.E_NOTIMPL;
        }

        public int get_CanonicalName(out string pbstrCanonicalName)
        {
            IVsProjectCfg projCfg = _innerCfg as IVsProjectCfg;
            if (projCfg != null)
            {
                return projCfg.get_CanonicalName(out pbstrCanonicalName);
            }
            pbstrCanonicalName = null;
            return VSConstants.E_NOTIMPL;
        }

        public int get_IsPackaged(out int pfIsPackaged)
        {
            IVsProjectCfg projCfg = _innerCfg as IVsProjectCfg;
            if (projCfg != null)
            {
                return projCfg.get_IsPackaged(out pfIsPackaged);
            }
            pfIsPackaged = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int get_IsSpecifyingOutputSupported(out int pfIsSpecifyingOutputSupported)
        {
            IVsProjectCfg projCfg = _innerCfg as IVsProjectCfg;
            if (projCfg != null)
            {
                return projCfg.get_IsSpecifyingOutputSupported(out pfIsSpecifyingOutputSupported);
            }
            pfIsSpecifyingOutputSupported = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int get_Platform(out Guid pguidPlatform)
        {
            IVsProjectCfg projCfg = _innerCfg as IVsProjectCfg;
            if (projCfg != null)
            {
                return projCfg.get_Platform(out pguidPlatform);
            }
            pguidPlatform = Guid.Empty;
            return VSConstants.E_NOTIMPL;
        }

        public int get_ProjectCfgProvider(out IVsProjectCfgProvider ppIVsProjectCfgProvider)
        {
            IVsProjectCfg projCfg = _innerCfg as IVsProjectCfg;
            if (projCfg != null)
            {
                return projCfg.get_ProjectCfgProvider(out ppIVsProjectCfgProvider);
            }
            ppIVsProjectCfgProvider = null;
            return VSConstants.E_NOTIMPL;
        }

        public int get_RootURL(out string pbstrRootURL)
        {
            IVsProjectCfg projCfg = _innerCfg as IVsProjectCfg;
            if (projCfg != null)
            {
                return projCfg.get_RootURL(out pbstrRootURL);
            }
            pbstrRootURL = null;
            return VSConstants.E_NOTIMPL;
        }

        public int get_TargetCodePage(out uint puiTargetCodePage)
        {
            IVsProjectCfg projCfg = _innerCfg as IVsProjectCfg;
            if (projCfg != null)
            {
                return projCfg.get_TargetCodePage(out puiTargetCodePage);
            }
            puiTargetCodePage = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int get_UpdateSequenceNumber(ULARGE_INTEGER[] puliUSN)
        {
            IVsProjectCfg projCfg = _innerCfg as IVsProjectCfg;
            if (projCfg != null)
            {
                return projCfg.get_UpdateSequenceNumber(puliUSN);
            }
            return VSConstants.E_NOTIMPL;
        }

        #endregion

        #region IVsProjectCfg2 Members

        public int OpenOutputGroup(string szCanonicalName, out IVsOutputGroup ppIVsOutputGroup)
        {
            IVsProjectCfg2 projCfg = _baseCfg as IVsProjectCfg2;
            if (projCfg != null)
            {
                return projCfg.OpenOutputGroup(szCanonicalName, out ppIVsOutputGroup);
            }
            ppIVsOutputGroup = null;
            return VSConstants.E_NOTIMPL;
        }

        public int OutputsRequireAppRoot(out int pfRequiresAppRoot)
        {
            IVsProjectCfg2 projCfg = _baseCfg as IVsProjectCfg2;
            if (projCfg != null)
            {
                return projCfg.OutputsRequireAppRoot(out pfRequiresAppRoot);
            }
            pfRequiresAppRoot = 1;
            return VSConstants.E_NOTIMPL;
        }

        public int get_CfgType(ref Guid iidCfg, out IntPtr ppCfg)
        {
            ppCfg = IntPtr.Zero;

            // See if this is an interface we support
            if (iidCfg == typeof(IVsDebuggableProjectCfg).GUID)
            {
                ppCfg = Marshal.GetComInterfaceForObject(this, typeof(IVsDebuggableProjectCfg));
            }
            else if (iidCfg == typeof(IVsDeployableProjectCfg).GUID)
            {
                ppCfg = Marshal.GetComInterfaceForObject(this, typeof(IVsDeployableProjectCfg));
            }
            else if (iidCfg == typeof(IVsQueryDebuggableProjectCfg).GUID)
            {
                ppCfg = Marshal.GetComInterfaceForObject(this, typeof(IVsQueryDebuggableProjectCfg));
            }
            else if (iidCfg == typeof(IVsBuildableProjectCfg).GUID)
            {
                IVsBuildableProjectCfg buildableConfig;
                this.get_BuildableProjectCfg(out buildableConfig);
                //
                //In some cases we've intentionally shutdown the build options
                //  If buildableConfig is null then don't try to get the BuildableProjectCfg interface
                //  
                if (null != buildableConfig)
                {
                    ppCfg = Marshal.GetComInterfaceForObject(buildableConfig, typeof(IVsBuildableProjectCfg));
                }
            }

            // If not supported
            if (ppCfg == IntPtr.Zero)
                return VSConstants.E_NOINTERFACE;

            return VSConstants.S_OK;
        }

        public int get_IsPrivate(out int pfPrivate)
        {
            IVsProjectCfg2 projCfg = _baseCfg as IVsProjectCfg2;
            if (projCfg != null)
            {
                return projCfg.get_IsPrivate(out pfPrivate);
            }
            pfPrivate = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int get_OutputGroups(uint celt, IVsOutputGroup[] rgpcfg, uint[] pcActual = null)
        {
            IVsProjectCfg2 projCfg = _baseCfg as IVsProjectCfg2;
            if (projCfg != null)
            {
                return projCfg.get_OutputGroups(celt, rgpcfg, pcActual);
            }
            return VSConstants.E_NOTIMPL;
        }

        public int get_VirtualRoot(out string pbstrVRoot)
        {
            IVsProjectCfg2 projCfg = _baseCfg as IVsProjectCfg2;
            if (projCfg != null)
            {
                return projCfg.get_VirtualRoot(out pbstrVRoot);
            }
            pbstrVRoot = null;
            return VSConstants.E_NOTIMPL;
        }

        #endregion

        #region IVsProjectFlavorCfg Members

        public int Close()
        {
            mapIVsCfgToNodejsUwpProjectFlavorCfg.Remove(_baseCfg);

            IVsProjectFlavorCfg cfg = _innerCfg as IVsProjectFlavorCfg;
            if (cfg != null)
            {
                return cfg.Close();
            }
            return VSConstants.S_OK;
        }

        #endregion

        #region IVsDebuggableProjectCfg Members

        public int DebugLaunch(uint flags)
        {
            VsDebugTargetInfo2[] targets;
            int hr = QueryDebugTargets(out targets);
            if (ErrorHandler.Failed(hr))
                return hr;

            flags |= (uint)__VSDBGLAUNCHFLAGS140.DBGLAUNCH_ContainsStartupTask;

            VsDebugTargetInfo4[] appPackageDebugTarget = new VsDebugTargetInfo4[1];
            int targetLength = (int)Marshal.SizeOf(typeof(VsDebugTargetInfo4));

            this.CopyDebugTargetInfo(ref targets[0], ref appPackageDebugTarget[0], flags);
            if (TargetType.Phone == ActiveTargetType || TargetType.Remote == ActiveTargetType)
            {
                IVsAppContainerBootstrapperResult result = this.BootstrapForDebuggingSync(targets[0].bstrRemoteMachine);
                appPackageDebugTarget[0].bstrRemoteMachine = result.Address;
            }
            else if (TargetType.Local == ActiveTargetType)
            {
                appPackageDebugTarget[0].bstrRemoteMachine = targets[0].bstrRemoteMachine;
            }

            // Pass the debug launch targets to the debugger
            IVsDebugger4 debugger4 = (IVsDebugger4)serviceProvider.GetService(typeof(SVsShellDebugger));
            VsDebugTargetProcessInfo[] results = new VsDebugTargetProcessInfo[1];

            debugger4.LaunchDebugTargets4(1, appPackageDebugTarget, results);

            return VSConstants.S_OK;
        }

        public int QueryDebugLaunch(uint grfLaunch, out int pfCanLaunch)
        {
            IVsDebuggableProjectCfg cfg = _baseCfg as IVsDebuggableProjectCfg;
            if (cfg != null)
            {
                return cfg.QueryDebugLaunch(grfLaunch, out pfCanLaunch);
            }
            pfCanLaunch = 0;
            return VSConstants.E_NOTIMPL;
        }

        #endregion

        #region ISpecifyPropertyPages Members

        public void GetPages(CAUUID[] pPages)
        {
            var cfg = _baseCfg as ISpecifyPropertyPages;
            if (cfg != null)
            {
                cfg.GetPages(pPages);
            }
        }

        #endregion

        #region IVsSpecifyProjectDesignerPages Members

        public int GetProjectDesignerPages(CAUUID[] pPages)
        {
            var cfg = _baseCfg as IVsSpecifyProjectDesignerPages;
            if (cfg != null)
            {
                return cfg.GetProjectDesignerPages(pPages);
            }
            return VSConstants.E_NOTIMPL;
        }

        #endregion

        #region IVsCfgBrowseObject Members

        public int GetCfg(out IVsCfg ppCfg)
        {
            ppCfg = this;
            return VSConstants.S_OK;
        }

        public int GetProjectItem(out IVsHierarchy pHier, out uint pItemid)
        {
            var cfg = _baseCfg as IVsCfgBrowseObject;
            if (cfg != null)
            {
                return cfg.GetProjectItem(out pHier, out pItemid);
            }
            pHier = null;
            pItemid = 0;
            return VSConstants.E_NOTIMPL;
        }

        #endregion

        #region IVsDeployableProjectCfg Members

        int IVsDeployableProjectCfg.AdviseDeployStatusCallback(IVsDeployStatusCallback pIVsDeployStatusCallback, out uint pdwCookie)
        {
            if (pIVsDeployStatusCallback == null)
            {
                pdwCookie = 0;
                return VSConstants.E_UNEXPECTED;
            }

            lock (syncObject)
            {
                pdwCookie = deployCallbackCollection.Add(pIVsDeployStatusCallback);
            }

            return VSConstants.S_OK;
        }

        int IVsDeployableProjectCfg.Commit(uint dwReserved)
        {
            return VSConstants.S_OK;
        }

        int IVsDeployableProjectCfg.QueryStartDeploy(uint dwOptions, int[] pfSupported, int[] pfReady)
        {
            if (pfSupported.Length > 0)
            {
                // Only Appx package producing appcontainer projects should support deployment
                pfSupported[0] = 1;
            }

            if (pfReady.Length > 0)
            {
                lock (syncObject)
                {
                    pfReady[0] = (appContainerDeployOperation == null && appContainerBootstrapperOperation == null) ? 1 : 0;
                }
            }

            return VSConstants.S_OK;
        }

        int IVsDeployableProjectCfg.QueryStatusDeploy(out int pfDeployDone)
        {
            lock (syncObject)
            {
                pfDeployDone = (appContainerDeployOperation == null && appContainerBootstrapperOperation == null) ? 1 : 0;
            }

            return VSConstants.S_OK;
        }

        int IVsDeployableProjectCfg.Rollback(uint dwReserved)
        {
            return VSConstants.S_OK;
        }

        int IVsDeployableProjectCfg.StartDeploy(IVsOutputWindowPane pIVsOutputWindowPane, uint dwOptions)
        {
            outputWindow = pIVsOutputWindowPane;

            if (!NotifyBeginDeploy())
            {
                return VSConstants.E_ABORT;
            }

            switch(ActiveTargetType)
            {
                case TargetType.Remote:
                    return RemoteDeploy();
                case TargetType.Local:
                    return LocalDeploy();
                case TargetType.Phone:
                    return PhoneDeploy();
                default:
                    return VSConstants.E_ABORT;            
            }

        }

        private void OnBootstrapEnd(string remoteMachine, IVsAppContainerBootstrapperResult result)
        {
            if (result == null || !result.Succeeded)
            {
                this.NotifyEndDeploy(0);
                return;
            }

            IVsDebuggerDeploy deploy = (IVsDebuggerDeploy)this.serviceProvider.GetService(typeof(SVsShellDebugger));
            int hr = deploy.ConnectToTargetComputer(result.Address, VsDebugRemoteAuthenticationMode.VSAUTHMODE_None, out this.debuggerDeployConnection);
            if (ErrorHandler.Failed(hr))
            {
                this.NotifyEndDeploy(0);
                return;
            }

            IVsAppContainerProjectDeploy2 deployHelper = (IVsAppContainerProjectDeploy2)this.serviceProvider.GetService(typeof(SVsAppContainerProjectDeploy));

            uint deployFlags = (uint)(_AppContainerDeployOptions.ACDO_NetworkLoopbackEnable | _AppContainerDeployOptions.ACDO_SetNetworkLoopback);

            if (!UpdatePackageJsonScript())
            {
                this.NotifyEndDeploy(0);
                return;
            }

            IVsAppContainerProjectDeployOperation localAppContainerDeployOperation = deployHelper.StartRemoteDeployAsync(deployFlags, this.debuggerDeployConnection, remoteMachine, this.GetRecipeFile(), this.GetProjectUniqueName(), this);
            lock (syncObject)
            {
                this.appContainerDeployOperation = localAppContainerDeployOperation;
            }
        }
        private int RemoteDeploy()
        {
            IVsAppContainerBootstrapper4 bootstrapper = (IVsAppContainerBootstrapper4)ServiceProvider.GlobalProvider.GetService(typeof(SVsAppContainerProjectDeploy));
            BootstrapMode bootStrapMode = BootstrapMode.UniversalBootstrapMode;

            VsDebugTargetInfo2[] targets;
            int hr = QueryDebugTargets(out targets);
            if (ErrorHandler.Failed(hr))
            {
                NotifyEndDeploy(0);
                return hr;
            }

            string projectUniqueName = GetProjectUniqueName();
            IVsTask localAppContainerBootstrapperOperation = bootstrapper.BootstrapAsync(projectUniqueName,
                                                                                         targets[0].bstrRemoteMachine,
                                                                                         bootStrapMode,
                                                                                         packagesToDeployList.Length,
                                                                                         packagesToDeployList,
                                                                                         optionalPackagesToDeploy.Length,
                                                                                         optionalPackagesToDeploy,
                                                                                         this);

            lock (syncObject)
            {
                this.appContainerBootstrapperOperation = localAppContainerBootstrapperOperation;
            }

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                IVsAppContainerBootstrapperResult result = null;
                try
                {
                    object taskResult = await localAppContainerBootstrapperOperation;
                    result = (IVsAppContainerBootstrapperResult)taskResult;
                }
                finally
                {
                    this.OnBootstrapEnd(targets[0].bstrRemoteMachine, result);
                }
            });

            return VSConstants.S_OK;
        }

        private int LocalDeploy()
        {
            IVsAppContainerProjectDeploy2 deployHelper = (IVsAppContainerProjectDeploy2)this.serviceProvider.GetService(typeof(SVsAppContainerProjectDeploy));

            uint deployFlags = (uint)(_AppContainerDeployOptions.ACDO_NetworkLoopbackEnable | _AppContainerDeployOptions.ACDO_SetNetworkLoopback);

            if (!UpdatePackageJsonScript())
            {
                this.NotifyEndDeploy(0);
                return VSConstants.E_ABORT;
            }

            string layoutDir = null;
            IVsBuildPropertyStorage bps = GetBuildPropertyStorage();
            bps.GetPropertyValue("LayoutDir", this.GetBaseCfgCanonicalName(), (uint)_PersistStorageType.PST_PROJECT_FILE, out layoutDir);

            IVsAppContainerProjectDeployOperation localAppContainerDeployOperation = deployHelper.StartDeployAsync(deployFlags, this.GetRecipeFile(), layoutDir, this.GetProjectUniqueName(), this);
            lock (syncObject)
            {
                this.appContainerDeployOperation = localAppContainerDeployOperation;
            }

            return VSConstants.S_OK;
        }

        private int PhoneDeploy()
        {
            IVsAppContainerBootstrapper3 bootstrapper = (IVsAppContainerBootstrapper3)ServiceProvider.GlobalProvider.GetService(typeof(SVsAppContainerProjectDeploy));

            string projectUniqueName = GetProjectUniqueName(); 
             IVsTask localAppContainerBootstrapperOperation = bootstrapper.BootstrapAsync(projectUniqueName,
                                                                                            DeviceIdString,
                                                                                            packagesToDeployList.Length,
                                                                                            packagesToDeployList,
                                                                                            optionalPackagesToDeploy.Length,
                                                                                            optionalPackagesToDeploy,
                                                                                            this);

            lock (syncObject)
            {
                this.appContainerBootstrapperOperation = localAppContainerBootstrapperOperation;
            }

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                IVsAppContainerBootstrapperResult result = null;
                try
                {
                    object taskResult = await localAppContainerBootstrapperOperation;
                    result = (IVsAppContainerBootstrapperResult)taskResult;
                }
                finally
                {
                    this.OnBootstrapEnd(DeviceIdString, result);
                }
            });

            return VSConstants.S_OK;
        }

        private IVsAppContainerBootstrapperResult BootstrapForDebuggingSync(string targetDevice)
        {
            if (TargetType.Phone == ActiveTargetType)
            {
                Device device = ConnectivityWrapper12.GetDevice(CultureInfo.CurrentCulture.LCID, DeviceIdString);
                PlatformInfo platform = ConnectivityWrapper12.GetPlatformInfo(device);

                ConnectivityWrapper12.CreateConnectedDeviceInstance(device);
                targetDevice = device.Id.ToString();

                IVsAppContainerBootstrapper bootstrapper = (IVsAppContainerBootstrapper)ServiceProvider.GlobalProvider.GetService(typeof(SVsAppContainerProjectDeploy));
                return (IVsAppContainerBootstrapperResult)bootstrapper.BootstrapForDebuggingAsync(GetProjectUniqueName(), targetDevice, this.GetRecipeFile(), logger: null).GetResult();
            }


            IVsAppContainerBootstrapper4 bootstrapper4 = (IVsAppContainerBootstrapper4)ServiceProvider.GlobalProvider.GetService(typeof(SVsAppContainerProjectDeploy));
            return (IVsAppContainerBootstrapperResult)bootstrapper4.BootstrapForDebuggingAsync(this.GetProjectUniqueName(), targetDevice, BootstrapMode.UniversalBootstrapMode, this.GetRecipeFile(), logger: null).GetResult();
        }

        private string GetProjectUniqueName()
        {
            string projectUniqueName = null;

            IVsSolution vsSolution = Package.GetGlobalService(typeof(SVsSolution)) as IVsSolution;
            if (vsSolution != null)
            {
                int hr = vsSolution.GetUniqueNameOfProject(this.NodeConfig, out projectUniqueName);
            }

            if (projectUniqueName == null)
            {
                throw new Exception("Failed to get an unique project name.");
            }
            return projectUniqueName;
        }

        private string GetRecipeFile()
        {
            string recipeFile = this.GetStringPropertyValue("AppxPackageRecipe");
            if (recipeFile == null)
            {
                string targetDir = this.GetStringPropertyValue("TargetDir");
                string projectName = this.GetStringPropertyValue("ProjectName");
                recipeFile = System.IO.Path.Combine(targetDir, projectName + ".appxrecipe");
            }

            return recipeFile;
        }

        internal string GetStringPropertyValue(string propertyName)
        {
            IVsBuildPropertyStorage bps = GetBuildPropertyStorage();
            string property = null;
            bps.GetPropertyValue(propertyName, this.GetBaseCfgCanonicalName(), (uint)_PersistStorageType.PST_PROJECT_FILE, out property);
            return property;
        }

        private void CopyDebugTargetInfo(ref VsDebugTargetInfo2 source, ref VsDebugTargetInfo4 destination, uint grfLaunch)
        {
            destination.AppPackageLaunchInfo.AppUserModelID = this.deployAppUserModelID;
            destination.AppPackageLaunchInfo.PackageMoniker = this.deployPackageMoniker;
            destination.dlo = (uint)_DEBUG_LAUNCH_OPERATION4.DLO_AppPackageDebug;
            destination.LaunchFlags = grfLaunch;
            destination.bstrRemoteMachine = "###HOSTNAME_PLACEHOLDER###";
            destination.bstrExe = source.bstrExe;
            destination.bstrArg = source.bstrArg;
            destination.bstrCurDir = source.bstrCurDir;
            destination.bstrEnv = source.bstrEnv;
            destination.dwProcessId = source.dwProcessId;
            destination.pStartupInfo = IntPtr.Zero;
            destination.guidLaunchDebugEngine = source.guidLaunchDebugEngine;
            destination.dwDebugEngineCount = source.dwDebugEngineCount;
            destination.pDebugEngines = source.pDebugEngines;
            destination.guidPortSupplier = VSConstants.DebugPortSupplierGuids.NoAuth_guid;
            destination.bstrPortName = source.bstrPortName;
            destination.bstrOptions = source.bstrOptions;
            destination.fSendToOutputWindow = source.fSendToOutputWindow;
            destination.pUnknown = source.pUnknown;
            destination.guidProcessLanguage = source.guidProcessLanguage;
            IVsSolution solution = (IVsSolution)Package.GetGlobalService(typeof(SVsSolution));
            IVsHierarchy hierarchy;
            solution.GetProjectOfUniqueName(GetProjectUniqueName(), out hierarchy);
            destination.project = hierarchy;
        }

        private string GetBaseCfgCanonicalName()
        {
            IVsProjectCfg projectCfg = _baseCfg as IVsProjectCfg;
            if (projectCfg == null)
            {
                return null;
            }

            string baseCfgCanonicalName;

            projectCfg.get_CanonicalName(out baseCfgCanonicalName);

            return baseCfgCanonicalName;
        }

        int IVsDeployableProjectCfg.StopDeploy(int fSync)
        {
            IVsTask localAppContainerBootstrapperOperation = null;
            IVsAppContainerProjectDeployOperation localAppContainerDeployOperation = null;
            lock (syncObject)
            {
                localAppContainerBootstrapperOperation = this.appContainerBootstrapperOperation;
                localAppContainerDeployOperation = this.appContainerDeployOperation;
                this.appContainerBootstrapperOperation = null;
                this.appContainerDeployOperation = null;
            }

            if (localAppContainerBootstrapperOperation != null)
            {
                localAppContainerBootstrapperOperation.Cancel();
                if (fSync != 0)
                {
                    try
                    {
                        localAppContainerBootstrapperOperation.Wait();
                    }
                    catch
                    {
                    }
                }
            }

            if (localAppContainerDeployOperation != null)
            {
                localAppContainerDeployOperation.StopDeploy(fSync != 0);
            }

            return VSConstants.S_OK;
        }

        int IVsDeployableProjectCfg.UnadviseDeployStatusCallback(uint dwCookie)
        {
            lock (syncObject)
            {
                deployCallbackCollection.RemoveAt(dwCookie);
            }

            return VSConstants.S_OK;
        }

        int IVsDeployableProjectCfg.WaitDeploy(uint dwMilliseconds, int fTickWhenMessageQNotEmpty)
        {
            return VSConstants.S_OK;
        }

        #endregion

        #region IVsProjectCfgDebugTargetSelection Members
        void IVsProjectCfgDebugTargetSelection.GetCurrentDebugTarget(out Guid pguidDebugTargetType, out uint pDebugTargetTypeId, out string pbstrCurrentDebugTarget)
        {
            IVsBuildPropertyStorage bps = GetBuildPropertyStorage();
            string property = null;
            pguidDebugTargetType = VSConstants.AppPackageDebugTargets.guidAppPackageDebugTargetCmdSet;
            pbstrCurrentDebugTarget = string.Empty;
            pDebugTargetTypeId = 0;

            bps.GetPropertyValue("DeployTarget", this.GetBaseCfgCanonicalName(), (uint)_PersistStorageType.PST_PROJECT_FILE, out property);

            if(null == property)
            {
                return;
            }
            
            if (0 == string.Compare("remote", property.ToLower()))
            {
                pDebugTargetTypeId = VSConstants.AppPackageDebugTargets.cmdidAppPackage_RemoteMachine;
                pbstrCurrentDebugTarget = RemoteTarget;
                ActiveTargetType = TargetType.Remote;

            }
            else if (0 == string.Compare("local", property.ToLower()))
            {
                pDebugTargetTypeId = VSConstants.AppPackageDebugTargets.cmdidAppPackage_LocalMachine;
                pbstrCurrentDebugTarget = LocalTarget;
                ActiveTargetType = TargetType.Local;
            }
            else if (0 == string.Compare("phone", property.ToLower()))
            {
                pDebugTargetTypeId = VSConstants.AppPackageDebugTargets.cmdidAppPackage_Emulator;
                pbstrCurrentDebugTarget = PhoneTarget;
                ActiveTargetType = TargetType.Phone;
            }

            // GetCurrentDebugTarget will get called whenever the platform is changed so we also update package.json
            UpdatePackageJsonPlatform();
        }

        Array IVsProjectCfgDebugTargetSelection.GetDebugTargetListOfType(Guid guidDebugTargetType, uint debugTargetTypeId)
        {
            string[] result = new string[1];
            if (guidDebugTargetType != VSConstants.AppPackageDebugTargets.guidAppPackageDebugTargetCmdSet)
            {
                return new string[0];
            }

            switch (debugTargetTypeId)
            {
                case VSConstants.AppPackageDebugTargets.cmdidAppPackage_RemoteMachine:
                    result[0] = RemoteTarget;
                    break;
                case VSConstants.AppPackageDebugTargets.cmdidAppPackage_LocalMachine:
                    result[0] = LocalTarget;
                    break;
                case VSConstants.AppPackageDebugTargets.cmdidAppPackage_Emulator:
                    result[0] = PhoneTarget;
                    break;
                default:
                    return new string[0];
            }

            return result;
        }

        bool IVsProjectCfgDebugTargetSelection.HasDebugTargets(IVsDebugTargetSelectionService pDebugTargetSelectionService, out Array pbstrSupportedTargetCommandIDs)
        {
            pbstrSupportedTargetCommandIDs = new string[] {
                String.Join(":", VSConstants.AppPackageDebugTargets.guidAppPackageDebugTargetCmdSet, VSConstants.AppPackageDebugTargets.cmdidAppPackage_LocalMachine),
                String.Join(":", VSConstants.AppPackageDebugTargets.guidAppPackageDebugTargetCmdSet, VSConstants.AppPackageDebugTargets.cmdidAppPackage_RemoteMachine),
                String.Join(":", VSConstants.AppPackageDebugTargets.guidAppPackageDebugTargetCmdSet, VSConstants.AppPackageDebugTargets.cmdidAppPackage_Emulator)
            };

            return true;
        }

        void IVsProjectCfgDebugTargetSelection.SetCurrentDebugTarget(Guid guidDebugTargetType, uint debugTargetTypeId, string bstrCurrentDebugTarget)
        {
            IVsBuildPropertyStorage bps = GetBuildPropertyStorage();

            switch (debugTargetTypeId)
            {
                case VSConstants.AppPackageDebugTargets.cmdidAppPackage_RemoteMachine:
                    bps.SetPropertyValue("DeployTarget", this.GetBaseCfgCanonicalName(), (uint)_PersistStorageType.PST_PROJECT_FILE, "remote");
                    break;
                case VSConstants.AppPackageDebugTargets.cmdidAppPackage_LocalMachine:
                    bps.SetPropertyValue("DeployTarget", this.GetBaseCfgCanonicalName(), (uint)_PersistStorageType.PST_PROJECT_FILE, "local");
                    break;
                case VSConstants.AppPackageDebugTargets.cmdidAppPackage_Emulator:
                    bps.SetPropertyValue("DeployTarget", this.GetBaseCfgCanonicalName(), (uint)_PersistStorageType.PST_PROJECT_FILE, "phone");
                    break;
                default:
                    return;
            }
        }

        #endregion

        #region IVsAppContainerBootstrapperLogger

        public void OutputMessage(string bstrMessage)
        {
            if (this.outputWindow != null)
            {
                this.outputWindow.OutputString(bstrMessage);
            }
        }

        #endregion

        #region IVsAppContainerProjectDeployCallback Members

        void IVsAppContainerProjectDeployCallback.OnEndDeploy(bool successful, string deployedPackageMoniker, string deployedAppUserModelID)
        {
            try
            {
                if (successful)
                {
                    deployPackageMoniker = deployedPackageMoniker;
                    deployAppUserModelID = deployedAppUserModelID;
                    NotifyEndDeploy(1);
                }
                else
                {
                    deployPackageMoniker = null;
                    deployAppUserModelID = null;
                    NotifyEndDeploy(0);
                }
            }
            finally
            {
                IVsDebuggerDeployConnection localConnection = null;

                lock (syncObject)
                {
                    this.appContainerBootstrapperOperation = null;
                    this.appContainerDeployOperation = null;
                    localConnection = this.debuggerDeployConnection;
                    this.debuggerDeployConnection = null;
                }

                if (localConnection != null)
                {
                    localConnection.Dispose();
                }
            }
        }

        void IVsAppContainerProjectDeployCallback.OutputMessage(string message)
        {
            if (null != outputWindow)
            {
                outputWindow.OutputString(message);
            }
        }

        #endregion

        #region IVsBuildPropertyStorage Members

        int IVsBuildPropertyStorage.GetItemAttribute(uint item, string attributeName, out string attributeValue)
        {
            attributeValue = null;
            IVsBuildPropertyStorage cfg = _innerCfg as IVsBuildPropertyStorage;
            if (cfg != null)
            {
                return cfg.GetItemAttribute(item, attributeName, out attributeValue);
            }
            return VSConstants.S_OK;
        }

        int IVsBuildPropertyStorage.GetPropertyValue(string propertyName, string configName, uint storage, out string propertyValue)
        {
            propertyValue = null;
            IVsBuildPropertyStorage cfg = _innerCfg as IVsBuildPropertyStorage;
            if (cfg != null)
            {
                return cfg.GetPropertyValue(propertyName, configName, storage, out propertyValue);
            }
            return VSConstants.S_OK;
        }

        int IVsBuildPropertyStorage.RemoveProperty(string propertyName, string configName, uint storage)
        {
            return ((IVsBuildPropertyStorage)this).SetPropertyValue(propertyName, configName, storage, null);
        }

        int IVsBuildPropertyStorage.SetItemAttribute(uint item, string attributeName, string attributeValue)
        {
            IVsBuildPropertyStorage cfg = _innerCfg as IVsBuildPropertyStorage;
            if (cfg != null)
            {
                return cfg.SetItemAttribute(item, attributeName, attributeValue);
            }
            return VSConstants.S_OK;
        }

        int IVsBuildPropertyStorage.SetPropertyValue(string propertyName, string configName, uint storage, string propertyValue)
        {
            IVsBuildPropertyStorage cfg = _innerCfg as IVsBuildPropertyStorage;
            if (cfg != null)
            {
                return cfg.SetPropertyValue(propertyName, configName, storage, propertyValue);
            }
            return VSConstants.S_OK;
        }

        #endregion

        #region IVsQueryDebuggableProjectCfg Members
        int IVsQueryDebuggableProjectCfg.QueryDebugTargets(uint grfLaunch, uint cTargets, VsDebugTargetInfo2[] rgDebugTargetInfo, uint[] pcActual)
        {
            string debuggerMachineName;

            if (pcActual != null && pcActual.Length > 0)
            {
                pcActual[0] = 1;
            }

            grfLaunch |= (uint)__VSDBGLAUNCHFLAGS140.DBGLAUNCH_ContainsStartupTask;

            if (rgDebugTargetInfo != null && rgDebugTargetInfo.Length > 0)
            {
                IList<Guid> debugEngineGuids = new Guid[] { VSConstants.DebugEnginesGuids.Script_guid };

                rgDebugTargetInfo[0] = new VsDebugTargetInfo2();
  
                rgDebugTargetInfo[0].bstrExe = "nodeuwp.exe";
                propertiesList.TryGetValue(NodejsUwpConstants.DebuggerMachineName, out debuggerMachineName);
                if (TargetType.Remote == ActiveTargetType)
                {
                    rgDebugTargetInfo[0].bstrRemoteMachine = debuggerMachineName;
                }
                else if (TargetType.Local == ActiveTargetType)
                {
                    rgDebugTargetInfo[0].bstrRemoteMachine = Environment.MachineName;
                }
                else if (TargetType.Phone == ActiveTargetType)
                {
                    rgDebugTargetInfo[0].bstrRemoteMachine = DeviceIdString;
                }
                rgDebugTargetInfo[0].guidLaunchDebugEngine = debugEngineGuids[0];
                rgDebugTargetInfo[0].guidPortSupplier = NativePortSupplier;
                rgDebugTargetInfo[0].fSendToOutputWindow = 1;
                rgDebugTargetInfo[0].dwDebugEngineCount = (uint)debugEngineGuids.Count;
                rgDebugTargetInfo[0].pDebugEngines = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(Guid)) * debugEngineGuids.Count);

                for (var i = 0; i < debugEngineGuids.Count; i++)
                {
                    Marshal.StructureToPtr(debugEngineGuids[i],
                        IntPtr.Add(rgDebugTargetInfo[0].pDebugEngines, i * Marshal.SizeOf(typeof(Guid))),
                        false);
                }
            }

            return VSConstants.S_OK;
        }

        #endregion

        #region IPersistXMLFragment Members

        public int InitNew(ref Guid guidFlavor, uint storage)
        {
            //Return, if it is our guid.
            if (IsMyFlavorGuid(ref guidFlavor))
            {
                return VSConstants.S_OK;
            }

            //Forward the call to inner flavor(s).
            if (this._innerCfg != null
                && this._innerCfg is IPersistXMLFragment)
            {
                return ((IPersistXMLFragment)this._innerCfg)
                    .InitNew(ref guidFlavor, storage);
            }

            return VSConstants.S_OK;
        }

        public int IsFragmentDirty(uint storage, out int pfDirty)
        {
            pfDirty = 0;
            switch (storage)
            {
                // Specifies storage file type to project file.
                case (uint)_PersistStorageType.PST_PROJECT_FILE:
                    if (isDirty)
                    {
                        pfDirty |= 1;
                    }
                    break;

                // Specifies storage file type to user file.
                case (uint)_PersistStorageType.PST_USER_FILE:
                    // Do not store anything in the user file.
                    break;
            }

            // Forward the call to inner flavor(s) 
            if (pfDirty == 0 && this._innerCfg != null
                && this._innerCfg is IPersistXMLFragment)
            {
                return ((IPersistXMLFragment)this._innerCfg)
                    .IsFragmentDirty(storage, out pfDirty);
            }
            return VSConstants.S_OK;

        }

        public int Load(ref Guid guidFlavor, uint storage, string pszXMLFragment)
        {
            if (IsMyFlavorGuid(ref guidFlavor))
            {
                switch (storage)
                {
                    case (uint)_PersistStorageType.PST_PROJECT_FILE:
                        // Load our data from the XML fragment.
                        XmlDocument doc = new XmlDocument();
                        XmlNode node = doc.CreateElement(this.GetType().Name);
                        node.InnerXml = pszXMLFragment;
                        if (node == null || node.FirstChild == null)
                            break;

                        // Load all the properties
                        foreach (XmlNode child in node.FirstChild.ChildNodes)
                        {
                            propertiesList.Add(child.Name, child.InnerText);
                        }
                        break;
                    case (uint)_PersistStorageType.PST_USER_FILE:
                        // Do not store anything in the user file.
                        break;
                }
            }

            // Forward the call to inner flavor(s)
            if (this._innerCfg != null
                && this._innerCfg is IPersistXMLFragment)
            {
                return ((IPersistXMLFragment)this._innerCfg)
                    .Load(ref guidFlavor, storage, pszXMLFragment);
            }

            return VSConstants.S_OK;

        }

        public int Save(ref Guid guidFlavor, uint storage, out string pbstrXMLFragment, int fClearDirty)
        {
            pbstrXMLFragment = null;

            if (IsMyFlavorGuid(ref guidFlavor))
            {
                switch (storage)
                {
                    case (uint)_PersistStorageType.PST_PROJECT_FILE:
                        // Create XML for our data (a string and a bool).
                        XmlDocument doc = new XmlDocument();
                        XmlNode root = doc.CreateElement(this.GetType().Name);

                        foreach (KeyValuePair<string, string> property in propertiesList)
                        {
                            XmlNode node = doc.CreateElement(property.Key);
                            node.AppendChild(doc.CreateTextNode(property.Value));
                            root.AppendChild(node);
                        }

                        doc.AppendChild(root);
                        // Get XML fragment representing our data
                        pbstrXMLFragment = doc.InnerXml;

                        if (fClearDirty != 0)
                            isDirty = false;
                        break;
                    case (uint)_PersistStorageType.PST_USER_FILE:
                        // Do not store anything in the user file.
                        break;
                }
            }

            // Forward the call to inner flavor(s)
            if (this._innerCfg != null
                && this._innerCfg is IPersistXMLFragment)
            {
                return ((IPersistXMLFragment)this._innerCfg)
                    .Save(ref guidFlavor, storage, out pbstrXMLFragment, fClearDirty);
            }

            return VSConstants.S_OK;
        }

        #endregion

        private bool IsMyFlavorGuid(ref Guid guidFlavor)
        {
            return guidFlavor.Equals(
                GuidList.guidNodejsUwpProjectFactory);
        }

        internal int QueryDebugTargets(out VsDebugTargetInfo2[] targets)
        {
            IntPtr queryDebuggableProjectCfgPtr = IntPtr.Zero;
            targets = null;

            Guid guid = typeof(IVsQueryDebuggableProjectCfg).GUID;
            int hr = get_CfgType(ref guid, out queryDebuggableProjectCfgPtr);
            if (ErrorHandler.Failed(hr))
                return hr;

            object queryDebuggableProjectCfgObject = Marshal.GetObjectForIUnknown(queryDebuggableProjectCfgPtr);
            if (queryDebuggableProjectCfgObject == null)
                return VSConstants.E_UNEXPECTED;

            IVsQueryDebuggableProjectCfg baseQueryDebugbableCfg = queryDebuggableProjectCfgObject as IVsQueryDebuggableProjectCfg;
            if (baseQueryDebugbableCfg == null)
                return VSConstants.E_UNEXPECTED;

            uint[] targetsCountOutput = new uint[1];
            hr = baseQueryDebugbableCfg.QueryDebugTargets(0, 0, null, targetsCountOutput);
            if (ErrorHandler.Failed(hr))
                return hr;
            uint numberOfDebugTargets = targetsCountOutput[0];

            targets = new VsDebugTargetInfo2[numberOfDebugTargets];
            hr = baseQueryDebugbableCfg.QueryDebugTargets(0, numberOfDebugTargets, targets, null);
            if (ErrorHandler.Failed(hr))
                return hr;

            if (string.IsNullOrEmpty(targets[0].bstrRemoteMachine))
            {
                MessageBox.Show(
                    "The project cannot be deployed or debugged because there is no remote machine specified in project settings.",
                    "Node Tools for Visual Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return VSConstants.E_ABORT;
            }

            return hr;
        }

        private bool NotifyBeginDeploy()
        {
            foreach (IVsDeployStatusCallback callback in GetSinkCollection())
            {
                if (callback == null)
                {
                    continue;
                }

                int fContinue = 1;

                if (ErrorHandler.Failed(callback.OnStartDeploy(ref fContinue)) || fContinue == 0)
                {
                    return false;
                }
            }

            return true;
        }

        private void NotifyEndDeploy(int fSuccess)
        {
            try
            {
                foreach (IVsDeployStatusCallback callback in GetSinkCollection())
                {
                    callback.OnEndDeploy(fSuccess);
                }
            }
            finally
            {
                lock (syncObject)
                {
                    this.appContainerBootstrapperOperation = null;
                    this.appContainerDeployOperation = null;
                }
            }
        }

        private IEnumerable<IVsDeployStatusCallback> GetSinkCollection()
        {
            List<IVsDeployStatusCallback> toNotify = null;

            lock (syncObject)
            {
                toNotify = new List<IVsDeployStatusCallback>(this.deployCallbackCollection.Count);
                foreach (IVsDeployStatusCallback callback in this.deployCallbackCollection)
                {
                    toNotify.Add(callback);
                }
            }

            return toNotify;
        }

        private bool UpdatePackageJsonScript()
        {
            string path = Path.Combine(GetStringPropertyValue("ProjectDir"), "package.json");
            JObject json = LoadPackageJson(path);
            if(null == json)
            {
                return false;
            }

            // Get startup values to add to package.json
            string nodeArgs;
            string scriptArgs;
            propertiesList.TryGetValue(NodejsUwpConstants.NodeExeArguments, out nodeArgs);
            propertiesList.TryGetValue(NodejsUwpConstants.ScriptArguments, out scriptArgs);
            string startupFile = GetStringPropertyValue("StartupFile");

            if (string.IsNullOrEmpty(startupFile))
            {
                MessageBox.Show("A startup file for the project has not been selected.", "NTVS IoT Extension",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            string startArgs = string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", nodeArgs, startupFile, scriptArgs);
            startArgs = startArgs.Trim();

            // Add startup values to package.json
            if (null != json["scripts"])
            {
                json.Property("scripts").Remove();
            }

            JObject scriptsObj = new JObject();
            scriptsObj.Add("start", startArgs);
            json.Add("scripts", scriptsObj);

            return SavePackageJson(path, json);
        }

        private bool UpdatePackageJsonPlatform()
        {
            string path = Path.Combine(GetStringPropertyValue("ProjectDir"), "package.json");
            JObject json = LoadPackageJson(path);
            if (null == json)
            {
                return false;
            }

            // Add platform to package.json for npm to use
            json["target_arch"] = GetPlatform();

            return SavePackageJson(path, json);
        }

        JObject LoadPackageJson(string path)
        {          
            JObject json;
            try
            {
                json = JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "NTVS IoT Extension", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            return json;
        }

        bool SavePackageJson(string path, JObject json)
        {
            try
            {
                File.WriteAllText(path, json.ToString());
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "NTVS IoT Extension", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }

        public string this[string propertyName]
        {
            get
            {
                if (propertiesList.ContainsKey(propertyName))
                {
                    return propertiesList[propertyName];
                }
                return String.Empty;
            }
            set
            {
                // Don't do anything if there isn't any real change
                if (this[propertyName] == value)
                {
                    return;
                }

                isDirty = true;
                if (propertiesList.ContainsKey(propertyName))
                {
                    propertiesList.Remove(propertyName);
                }
                propertiesList.Add(propertyName, value);
            }
        }

        internal static NodejsUwpProjectFlavorCfg GetPropertyPageFromIVsCfg(IVsCfg configuration)
        {
            if (mapIVsCfgToNodejsUwpProjectFlavorCfg.ContainsKey(configuration))
            {
                return (NodejsUwpProjectFlavorCfg)mapIVsCfgToNodejsUwpProjectFlavorCfg[configuration];
            }
            else
            {
                throw new ArgumentOutOfRangeException("Cannot find configuration in mapIVsCfgToSpecializedCfg.");
            }
        }

        private IVsBuildPropertyStorage GetBuildPropertyStorage()
        {
            IVsSolution solution = (IVsSolution)Package.GetGlobalService(typeof(SVsSolution));
            IVsHierarchy hierarchy;
            solution.GetProjectOfUniqueName(GetProjectUniqueName(), out hierarchy);
            IVsBuildPropertyStorage bps = hierarchy as IVsBuildPropertyStorage;
            return bps;
        }

        private string GetPlatform()
        {
            EnvDTE80.DTE2 _applicationObject = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            EnvDTE80.Solution2 solution2 = (EnvDTE80.Solution2)_applicationObject.Solution;
            EnvDTE80.SolutionBuild2 solutionBuild2 = (EnvDTE80.SolutionBuild2)solution2.SolutionBuild;
            EnvDTE80.SolutionConfiguration2 solutionConfiguration2 = (EnvDTE80.SolutionConfiguration2)solutionBuild2.ActiveConfiguration;
            return solutionConfiguration2.PlatformName;
        }
    }
}
