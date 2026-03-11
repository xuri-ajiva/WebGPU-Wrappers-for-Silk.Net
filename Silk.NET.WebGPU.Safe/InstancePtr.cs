using System;
using Silk.NET.Core.Native;
using System.Threading.Tasks;
using Silk.NET.WebGPU.Safe.Utils;
using System.Dynamic;
using Silk.NET.Core.Attributes;
using Silk.NET.Core;

namespace Silk.NET.WebGPU.Safe
{
    public static class WebGPUExtensions
    {
        public static unsafe InstancePtr CreateInstance(this WebGPU wgpu)
        {
            InstanceDescriptor descriptor = new();
            return new(wgpu, wgpu.CreateInstance(ref descriptor));
        }
    }

    public unsafe partial struct InstancePtr
    {
        private static readonly RentalStorage<(WebGPU, TaskCompletionSource<AdapterPtr>)> s_adapterRequests = new();

        private static void AdapterRequestCallback(RequestAdapterStatus status, Adapter* adapter, byte* message,
            void* data)
        {
            var (wgpu, task) = s_adapterRequests.GetAndReturn((int)data);

            if (status != RequestAdapterStatus.Success)
            {
                task.SetException(new WGPUException(
                    $"{status} {SilkMarshal.PtrToString((nint)message, NativeStringEncoding.UTF8)}"));

                return;
            }

            task.SetResult(new AdapterPtr(wgpu, adapter));
        }

        private readonly PfnRequestAdapterCallback
            s_AdapterRequestCallback = new(AdapterRequestCallback);

        private readonly WebGPU _wgpu;
        private readonly Instance* _ptr;

        public InstancePtr(WebGPU wgpu, Instance* ptr)
        {
            _wgpu = wgpu;
            _ptr = ptr;
        }

        public static implicit operator Instance*(InstancePtr ptr) => ptr._ptr;

        #region CreateSurface

        public SurfacePtr CreateSurfaceFromAndroidNativeWindow(IntPtr nativeWindow, string? label = null)
        {
            var descriptor = new SurfaceDescriptorFromAndroidNativeWindow
            {
                Chain = new ChainedStruct(sType: SType.SurfaceDescriptorFromAndroidNativeWindow),
                Window = (void*)nativeWindow
            };
            using var marshalledLabel = new MarshalledString(label, NativeStringEncoding.UTF8);

            var surfaceDescriptor = new SurfaceDescriptor(
                label: marshalledLabel.Ptr,
                nextInChain: &descriptor.Chain
            );
            return new SurfacePtr(_wgpu, _wgpu.InstanceCreateSurface(_ptr, in surfaceDescriptor));
        }

        public SurfacePtr CreateSurfaceFromHTMLCanvas(string selector, string? label = null)
        {
            using var marshalledSelector = new MarshalledString(selector, NativeStringEncoding.UTF8);

            var descriptor = new SurfaceDescriptorFromCanvasHTMLSelector
            {
                Chain = new ChainedStruct(sType: SType.SurfaceDescriptorFromCanvasHtmlSelector),
                Selector = marshalledSelector.Ptr
            };
            using var marshalledLabel = new MarshalledString(label, NativeStringEncoding.UTF8);

            var surfaceDescriptor = new SurfaceDescriptor(
                label: marshalledLabel.Ptr,
                nextInChain: &descriptor.Chain
            );
            return new SurfacePtr(_wgpu, _wgpu.InstanceCreateSurface(_ptr, in surfaceDescriptor));
        }

        public SurfacePtr CreateSurfaceFromMetalLayer(IntPtr layer, string? label = null)
        {
            var descriptor = new SurfaceDescriptorFromMetalLayer
            {
                Chain = new ChainedStruct(sType: SType.SurfaceDescriptorFromMetalLayer),
                Layer = (void*)layer
            };
            using var marshalledLabel = new MarshalledString(label, NativeStringEncoding.UTF8);

            var surfaceDescriptor = new SurfaceDescriptor(
                label: marshalledLabel.Ptr,
                nextInChain: &descriptor.Chain
            );
            return new SurfacePtr(_wgpu, _wgpu.InstanceCreateSurface(_ptr, in surfaceDescriptor));
        }

        public SurfacePtr CreateSurfaceFromWaylandSurface(IntPtr display, IntPtr surface, string? label = null)
        {
            var descriptor = new SurfaceDescriptorFromWaylandSurface
            {
                Chain = new ChainedStruct(sType: SType.SurfaceDescriptorFromWaylandSurface),
                Display = (void*)display,
                Surface = (void*)surface
            };
            using var marshalledLabel = new MarshalledString(label, NativeStringEncoding.UTF8);

            var surfaceDescriptor = new SurfaceDescriptor(
                label: marshalledLabel.Ptr,
                nextInChain: &descriptor.Chain
            );
            return new SurfacePtr(_wgpu, _wgpu.InstanceCreateSurface(_ptr, in surfaceDescriptor));
        }

        public SurfacePtr CreateSurfaceFromWindowsHWND(IntPtr hwnd, IntPtr? hInstance = null, string? label = null)
        {
            var descriptor = new SurfaceDescriptorFromWindowsHWND
            {
                Chain = new ChainedStruct(sType: SType.SurfaceDescriptorFromWindowsHwnd),
                Hwnd = (void*)hwnd,
                Hinstance = (void*)(hInstance ?? null),
            };
            using var marshalledLabel = new MarshalledString(label, NativeStringEncoding.UTF8);

            var surfaceDescriptor = new SurfaceDescriptor(
                label: marshalledLabel.Ptr,
                nextInChain: &descriptor.Chain
            );
            return new SurfacePtr(_wgpu, _wgpu.InstanceCreateSurface(_ptr, in surfaceDescriptor));
        }

        public SurfacePtr CreateSurfaceFromXcbWindow(IntPtr connection, uint window, string? label = null)
        {
            var descriptor = new SurfaceDescriptorFromXcbWindow
            {
                Chain = new ChainedStruct(sType: SType.SurfaceDescriptorFromXcbWindow),
                Connection = (void*)connection,
                Window = window
            };
            using var marshalledLabel = new MarshalledString(label, NativeStringEncoding.UTF8);

            var surfaceDescriptor = new SurfaceDescriptor(
                label: marshalledLabel.Ptr,
                nextInChain: &descriptor.Chain
            );
            return new SurfacePtr(_wgpu, _wgpu.InstanceCreateSurface(_ptr, in surfaceDescriptor));
        }

        public SurfacePtr CreateSurfaceFromXlibWindow(IntPtr display, uint window, string? label = null)
        {
            var descriptor = new SurfaceDescriptorFromXlibWindow
            {
                Chain = new ChainedStruct(sType: SType.SurfaceDescriptorFromXlibWindow),
                Display = (void*)display,
                Window = window
            };
            using var marshalledLabel = new MarshalledString(label, NativeStringEncoding.UTF8);

            var surfaceDescriptor = new SurfaceDescriptor(
                label: marshalledLabel.Ptr,
                nextInChain: &descriptor.Chain
            );
            return new SurfacePtr(_wgpu, _wgpu.InstanceCreateSurface(_ptr, in surfaceDescriptor));
        }

        #endregion

        public bool HasWGSLLanguageFeature(WGSLFeatureName feature)
        {
            return _wgpu.InstanceHasWGSLLanguageFeature(_ptr, feature);
        }

        public void ProcessEvents()
        {
            _wgpu.InstanceProcessEvents(_ptr);
        }

        public Task<AdapterPtr> RequestAdapter(SurfacePtr? compatibleSurface = null,
            PowerPreference powerPreference = default,
            BackendType backendType = default,
            Bool32 forceFallbackAdapter = default)
        {
            var task = new TaskCompletionSource<AdapterPtr>();
            int key = s_adapterRequests.Rent((_wgpu, task));
            var requestAdapterOptions = new RequestAdapterOptions
            {
                CompatibleSurface = compatibleSurface ?? (Surface*)null,
                PowerPreference = powerPreference,
                BackendType = backendType,
                ForceFallbackAdapter = forceFallbackAdapter,
            };
            _wgpu.InstanceRequestAdapter(_ptr, in
                requestAdapterOptions
                , s_AdapterRequestCallback, (void*)key);

            return task.Task;
        }

        public void Reference() => _wgpu.InstanceReference(_ptr);

        public void Release() => _wgpu.InstanceRelease(_ptr);
    }
}