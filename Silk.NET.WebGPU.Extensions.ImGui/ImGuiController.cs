using System;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.SDL3;
using static Silk.NET.WebGPU.Safe.BindGroupEntries;
using static Silk.NET.WebGPU.Safe.BindGroupLayoutEntries;
using Silk.NET.WebGPU.Safe;
using Silk.NET.Core.Native;

namespace Silk.NET.WebGPU.Extensions.ImGui
{
    record struct BufferRange(BufferPtr Buffer, ulong Offset, ulong Size);

    /// <summary>
    /// Can render draw lists produced by ImGui.
    /// Also provides functions for updating ImGui input.
    /// </summary>
    public class ImGuiController : IDisposable
    {
        private DevicePtr _device;
        private SDLWindowPtr _view;
        private readonly List<char> _pressedChars = new List<char>();

        // Device objects
        private BufferRange _vertexBuffer;
        private BufferRange _indexBuffer;
        private BufferPtr _projMatrixBuffer;
        private TexturePtr? _fontTexture;
        private ShaderModulePtr _shaderModule;
        private BindGroupLayoutPtr _layout;
        private BindGroupLayoutPtr _textureLayout;
        private RenderPipelinePtr _pipeline;
        private BindGroupPtr _mainBindGroup;
        private BindGroupPtr? _fontTextureResourceSet;
        private IntPtr _fontAtlasID = (IntPtr)1;

        public ImGuiContextPtr Context;

        private readonly Dictionary<TextureViewPtr, BindGroupPtr> _bindGroupsByView = new();
        private bool _frameBegun;
        private List<(ImGuiKey key, bool isDown)> _keyEvents = new();

        private static ulong NextValidBufferSize(ulong size) => (size + 15) / 16 * 16;

        /// <summary>
        /// Constructs a new ImGuiController.
        /// </summary>
        public ImGuiController(DevicePtr gd, TextureFormat colorOutputFormat, SDLWindowPtr view) : this(
            gd, colorOutputFormat, view, null, null)
        {
        }

        /// <summary>
        /// Constructs a new ImGuiController with font configuration.
        /// </summary>
        public ImGuiController(DevicePtr gd, TextureFormat colorOutputFormat, SDLWindowPtr view,
            ImGuiFontConfig imGuiFontConfig) : this(gd, colorOutputFormat, view, imGuiFontConfig, null)
        {
        }

        /// <summary>
        /// Constructs a new ImGuiController with an onConfigureIO Action.
        /// </summary>
        public ImGuiController(DevicePtr gd, TextureFormat colorOutputFormat, SDLWindowPtr view,
            Action onConfigureIO) : this(gd, colorOutputFormat, view, null, onConfigureIO)
        {
        }

        /// <summary>
        /// Constructs a new ImGuiController with font configuration and onConfigure Action.
        /// </summary>
        public unsafe ImGuiController(DevicePtr gd, TextureFormat colorOutputFormat, SDLWindowPtr view,
            ImGuiFontConfig? imGuiFontConfig = null, Action? onConfigureIO = null)
        {
            _device = gd;
            _view = view;

            Context = Hexa.NET.ImGui.ImGui.CreateContext();
            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            Hexa.NET.ImGui.ImGui.StyleColorsDark();

            var io = Hexa.NET.ImGui.ImGui.GetIO();

            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

            if (imGuiFontConfig is not null)
            {
                var glyphRange = imGuiFontConfig.Value.GetGlyphRange?.Invoke(io) ?? IntPtr.Zero;

                var fontPathPtr = SilkMarshal.StringToPtr(imGuiFontConfig.Value.FontPath);

                Hexa.NET.ImGui.ImGui.AddFontFromFileTTF(io.Fonts, (byte*)fontPathPtr, imGuiFontConfig.Value.FontSize,
                    new ImFontConfigPtr(), (uint*)glyphRange);

                SilkMarshal.Free(fontPathPtr);
            }

            onConfigureIO?.Invoke();


            CreateDeviceResources(gd, colorOutputFormat);

            SetPerFrameImGuiData(1f / 60f);

            BeginFrame();
        }

        private bool TryDecodeKey(SDLScancode keycode, out ImGuiKey decodedKey)
        {
            decodedKey = keycode switch
            {
                SDLScancode.Tab => ImGuiKey.Tab,
                SDLScancode.Left => ImGuiKey.LeftArrow,
                SDLScancode.Right => ImGuiKey.RightArrow,
                SDLScancode.Up => ImGuiKey.UpArrow,
                SDLScancode.Down => ImGuiKey.DownArrow,
                SDLScancode.Pageup => ImGuiKey.PageUp,
                SDLScancode.Pagedown => ImGuiKey.PageDown,
                SDLScancode.Home => ImGuiKey.Home,
                SDLScancode.End => ImGuiKey.End,
                SDLScancode.Insert => ImGuiKey.Insert,
                SDLScancode.Delete => ImGuiKey.Delete,
                SDLScancode.Backspace => ImGuiKey.Backspace,
                SDLScancode.Space => ImGuiKey.Space,
                SDLScancode.Return => ImGuiKey.Enter,
                SDLScancode.Escape => ImGuiKey.Escape,
                //SDLScancode.Quote => ImGuiKey.Apostrophe,
                SDLScancode.Comma => ImGuiKey.Comma,
                SDLScancode.Minus => ImGuiKey.Minus,
                SDLScancode.Period => ImGuiKey.Period,
                SDLScancode.Slash => ImGuiKey.Slash,
                SDLScancode.Semicolon => ImGuiKey.Semicolon,
                SDLScancode.Equals => ImGuiKey.Equal,
                SDLScancode.Leftbracket => ImGuiKey.LeftBracket,
                SDLScancode.Backslash => ImGuiKey.Backslash,
                SDLScancode.Rightbracket => ImGuiKey.RightBracket,
                //SDLScancode.Backquote => ImGuiKey.GraveAccent,
                SDLScancode.Capslock => ImGuiKey.CapsLock,
                SDLScancode.Scrolllock => ImGuiKey.ScrollLock,
                SDLScancode.Numlockclear => ImGuiKey.NumLock,
                SDLScancode.Printscreen => ImGuiKey.PrintScreen,
                SDLScancode.Pause => ImGuiKey.Pause,
                SDLScancode.Kp0 => ImGuiKey.Keypad0,
                SDLScancode.Kp1 => ImGuiKey.Keypad1,
                SDLScancode.Kp2 => ImGuiKey.Keypad2,
                SDLScancode.Kp3 => ImGuiKey.Keypad3,
                SDLScancode.Kp4 => ImGuiKey.Keypad4,
                SDLScancode.Kp5 => ImGuiKey.Keypad5,
                SDLScancode.Kp6 => ImGuiKey.Keypad6,
                SDLScancode.Kp7 => ImGuiKey.Keypad7,
                SDLScancode.Kp8 => ImGuiKey.Keypad8,
                SDLScancode.Kp9 => ImGuiKey.Keypad9,
                SDLScancode.KpPeriod => ImGuiKey.KeypadDecimal,
                SDLScancode.KpDivide => ImGuiKey.KeypadDivide,
                SDLScancode.KpMultiply => ImGuiKey.KeypadMultiply,
                SDLScancode.KpMinus => ImGuiKey.KeypadSubtract,
                SDLScancode.KpPlus => ImGuiKey.KeypadAdd,
                SDLScancode.KpEnter => ImGuiKey.KeypadEnter,
                SDLScancode.KpEquals => ImGuiKey.KeypadEqual,
                SDLScancode.Lctrl => ImGuiKey.LeftCtrl,
                SDLScancode.Lshift => ImGuiKey.LeftShift,
                SDLScancode.Lalt => ImGuiKey.LeftAlt,
                SDLScancode.Lgui => ImGuiKey.LeftSuper,
                SDLScancode.Rctrl => ImGuiKey.RightCtrl,
                SDLScancode.Rshift => ImGuiKey.RightShift,
                SDLScancode.Ralt => ImGuiKey.RightAlt,
                SDLScancode.Rgui => ImGuiKey.RightSuper,
                SDLScancode.Application => ImGuiKey.Menu,
                SDLScancode.Scancode0 => ImGuiKey.Key0,
                SDLScancode.Scancode1 => ImGuiKey.Key1,
                SDLScancode.Scancode2 => ImGuiKey.Key2,
                SDLScancode.Scancode3 => ImGuiKey.Key3,
                SDLScancode.Scancode4 => ImGuiKey.Key4,
                SDLScancode.Scancode5 => ImGuiKey.Key5,
                SDLScancode.Scancode6 => ImGuiKey.Key6,
                SDLScancode.Scancode7 => ImGuiKey.Key7,
                SDLScancode.Scancode8 => ImGuiKey.Key8,
                SDLScancode.Scancode9 => ImGuiKey.Key9,
                SDLScancode.A => ImGuiKey.A,
                SDLScancode.B => ImGuiKey.B,
                SDLScancode.C => ImGuiKey.C,
                SDLScancode.D => ImGuiKey.D,
                SDLScancode.E => ImGuiKey.E,
                SDLScancode.F => ImGuiKey.F,
                SDLScancode.G => ImGuiKey.G,
                SDLScancode.H => ImGuiKey.H,
                SDLScancode.I => ImGuiKey.I,
                SDLScancode.J => ImGuiKey.J,
                SDLScancode.K => ImGuiKey.K,
                SDLScancode.L => ImGuiKey.L,
                SDLScancode.M => ImGuiKey.M,
                SDLScancode.N => ImGuiKey.N,
                SDLScancode.O => ImGuiKey.O,
                SDLScancode.P => ImGuiKey.P,
                SDLScancode.Q => ImGuiKey.Q,
                SDLScancode.R => ImGuiKey.R,
                SDLScancode.S => ImGuiKey.S,
                SDLScancode.T => ImGuiKey.T,
                SDLScancode.U => ImGuiKey.U,
                SDLScancode.V => ImGuiKey.V,
                SDLScancode.W => ImGuiKey.W,
                SDLScancode.X => ImGuiKey.X,
                SDLScancode.Y => ImGuiKey.Y,
                SDLScancode.Z => ImGuiKey.Z,
                SDLScancode.F1 => ImGuiKey.F1,
                SDLScancode.F2 => ImGuiKey.F2,
                SDLScancode.F3 => ImGuiKey.F3,
                SDLScancode.F4 => ImGuiKey.F4,
                SDLScancode.F5 => ImGuiKey.F5,
                SDLScancode.F6 => ImGuiKey.F6,
                SDLScancode.F7 => ImGuiKey.F7,
                SDLScancode.F8 => ImGuiKey.F8,
                SDLScancode.F9 => ImGuiKey.F9,
                SDLScancode.F10 => ImGuiKey.F10,
                SDLScancode.F11 => ImGuiKey.F11,
                SDLScancode.F12 => ImGuiKey.F12,
                SDLScancode.AcBack => ImGuiKey.AppBack,
                SDLScancode.AcForward => ImGuiKey.AppForward,
                _ => ImGuiKey.None,
            };

            return decodedKey != ImGuiKey.None;
        }

        public void MakeCurrent()
        {
            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
        }

        private void BeginFrame()
        {
            Hexa.NET.ImGui.ImGui.NewFrame();
            _frameBegun = true;

            /*if (_keyboard == _input.Keyboards[0])
                return;

            if (_keyboard is not null)
            {
                _keyboard.KeyChar -= OnKeyChar;
                _keyboard.KeyDown -= OnKeyDown;
                _keyboard.KeyUp -= OnKeyUp;
            }

            _keyboard = _input.Keyboards[0];
            _keyboard.KeyChar += OnKeyChar;
            _keyboard.KeyDown += OnKeyDown;
            _keyboard.KeyUp += OnKeyUp;*/
        }

        /*private void OnKeyDown(IKeyboard arg1, Key arg2, int arg3)
        {
            if (TryDecodeKey(arg2, arg3, out var decodedKey))
                _keyEvents.Add((decodedKey, true));
        }

        private void OnKeyUp(IKeyboard arg1, Key arg2, int arg3)
        {
            if (TryDecodeKey(arg2, arg3, out var decodedKey))
                _keyEvents.Add((decodedKey, false));
        }

        private void OnKeyChar(IKeyboard arg1, char arg2)
        {
            _pressedChars.Add(arg2);
        }*/

        /// <summary>
        /// Renders the ImGui draw list data.
        /// This method requires a <see cref="GraphicsDevice"/> because it may create new DeviceBuffers if the size of vertex
        /// or index data has increased beyond the capacity of the existing buffers.
        /// A <see cref="CommandList"/> is needed to submit drawing and resource update commands.
        /// </summary>
        public void Render(RenderPassEncoderPtr pass)
        {
            if (_frameBegun)
            {
                var oldCtx = Hexa.NET.ImGui.ImGui.GetCurrentContext();

                if (oldCtx != Context)
                {
                    Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
                }

                _frameBegun = false;
                Hexa.NET.ImGui.ImGui.Render();
                Hexa.NET.ImGui.ImGui.EndFrame();
                RenderImDrawData(Hexa.NET.ImGui.ImGui.GetDrawData(), pass);

                if (oldCtx != Context)
                {
                    Hexa.NET.ImGui.ImGui.SetCurrentContext(oldCtx);
                }
            }
        }

        /// <summary>
        /// Updates ImGui input and IO configuration state.
        /// </summary>
        public void Update(float deltaSeconds)
        {
            var oldCtx = Hexa.NET.ImGui.ImGui.GetCurrentContext();

            if (oldCtx != Context)
            {
                Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            }

            if (_frameBegun)
            {
                Hexa.NET.ImGui.ImGui.Render();
            }

            SetPerFrameImGuiData(deltaSeconds);
            UpdateImGuiInput();

            _frameBegun = true;
            Hexa.NET.ImGui.ImGui.NewFrame();

            if (oldCtx != Context)
            {
                Hexa.NET.ImGui.ImGui.SetCurrentContext(oldCtx);
            }
        }

        /// <summary>
        /// Sets per-frame data based on the associated window.
        /// This is called by Update(float).
        /// </summary>
        private void SetPerFrameImGuiData(float deltaSeconds)
        {
            var io = Hexa.NET.ImGui.ImGui.GetIO();
            int x = 0, y = 0;
            SDL.GetWindowSizeInPixels(_view, ref x, ref y);
            io.DisplaySize = new Vector2(x, y);

            if (x * y > 0)
            {
                io.DisplayFramebufferScale = Vector2.One;
            }

            io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.
        }

        // private static Key[] keyEnumArr = (Key[])Enum.GetValues(typeof(Key));

        private void UpdateImGuiInput()
        {
            var io = Hexa.NET.ImGui.ImGui.GetIO();

            /*var mouseState = _input.Mice[0].CaptureState();
            var keyboardState = _input.Keyboards[0];

            io.MouseDown[0] = mouseState.IsButtonPressed(MouseButton.Left);
            io.MouseDown[1] = mouseState.IsButtonPressed(MouseButton.Right);
            io.MouseDown[2] = mouseState.IsButtonPressed(MouseButton.Middle);

            var point = new Vector2D<int>((int)mouseState.Position.X, (int)mouseState.Position.Y);
            io.MousePos = new Vector2(point.X, point.Y);

            var wheel = mouseState.GetScrollWheels()[0];
            io.MouseWheel = wheel.Y;
            io.MouseWheelH = wheel.X;
 
            foreach (var entry in _keyEvents)
            {
                io.AddKeyEvent(entry.key, entry.isDown);
            }

            _keyEvents.Clear();

            foreach (var c in _pressedChars)
            {
                io.AddInputCharacter(c);
            }

            _pressedChars.Clear();

            io.KeyCtrl = keyboardState.IsKeyPressed(Key.ControlLeft) || keyboardState.IsKeyPressed(Key.ControlRight);
            io.KeyAlt = keyboardState.IsKeyPressed(Key.AltLeft) || keyboardState.IsKeyPressed(Key.AltRight);
            io.KeyShift = keyboardState.IsKeyPressed(Key.ShiftLeft) || keyboardState.IsKeyPressed(Key.ShiftRight);
            io.KeySuper = keyboardState.IsKeyPressed(Key.SuperLeft) || keyboardState.IsKeyPressed(Key.SuperRight);*/
        }

        private void CreateDeviceResources(DevicePtr device, TextureFormat colorOutputFormat)
        {
            _device = device;
            _vertexBuffer = new(
                device.CreateBuffer(BufferUsage.Vertex | BufferUsage.CopyDst, 10000, label: "ImGui.NET Vertex Buffer"),
                Offset: 0, Size: 10000);
            _indexBuffer = new(
                device.CreateBuffer(BufferUsage.Index | BufferUsage.CopyDst, 2000, label: "ImGui.NET Index Buffer"),
                Offset: 0, Size: 2000);

            _projMatrixBuffer = device.CreateBuffer(BufferUsage.Uniform | BufferUsage.CopyDst, 64,
                label: "ImGui.NET Projection Buffer");

            Safe.VertexBufferLayout[] vertexLayouts = new Safe.VertexBufferLayout[]
            {
                new Safe.VertexBufferLayout
                {
                    ArrayStride = 5 * sizeof(float),
                    StepMode = VertexStepMode.Vertex,
                    Attributes = new VertexAttribute[]
                    {
                        //in_position
                        new(VertexFormat.Float32x2, offset: 0, shaderLocation: 0),
                        //in_texCoord
                        new(VertexFormat.Float32x2, offset: 2 * sizeof(float), shaderLocation: 1),
                        //in_color
                        new(VertexFormat.Unorm8x4, offset: 4 * sizeof(float), shaderLocation: 2)
                    }
                }
            };

            _layout = device.CreateBindGroupLayout(
                new BindGroupLayoutEntry[]
                {
                    Buffer(0, ShaderStage.Vertex, BufferBindingType.Uniform, 64),
                    Sampler(1, ShaderStage.Fragment, SamplerBindingType.Filtering)
                },
                label: "ImGui.NET Resource Layout"
            );

            _textureLayout = device.CreateBindGroupLayout(
                new BindGroupLayoutEntry[]
                {
                    Texture(2, ShaderStage.Fragment, TextureSampleType.Float, TextureViewDimension.Dimension2D,
                        multisampled: false)
                },
                label: "ImGui.NET Texture Layout"
            );

            SamplerPtr pointSampler = _device.CreateSampler(
                AddressMode.Repeat, AddressMode.Repeat, AddressMode.Repeat,
                FilterMode.Nearest, FilterMode.Nearest, MipmapFilterMode.Nearest,
                0, 0, default, 1,
                label: "ImGui.NET PointSampler");

            var pipelineLayout = _device.CreatePipelineLayout(
                new Safe.BindGroupLayoutPtr[]
                {
                    _layout,
                    _textureLayout
                }
            );

            var shaderCode = """
                                             struct Uniforms {
                                 u_Matrix: mat4x4<f32>
                             };

                             struct VertexInput {
                                 @location(0) a_Pos: vec2<f32>,
                                 @location(1) a_UV: vec2<f32>,
                                 @location(2) a_Color: vec4<f32>
                             };

                             struct VertexOutput {
                                 @location(0) v_UV: vec2<f32>,
                                 @location(1) v_Color: vec4<f32>,
                                 @builtin(position) v_Position: vec4<f32>
                             };

                             @group(0)
                             @binding(0)
                             var<uniform> uniforms: Uniforms;

                             @vertex
                             fn vs_main(in: VertexInput) -> VertexOutput {
                                 var out: VertexOutput;
                                 out.v_UV = in.a_UV;
                                 out.v_Color = in.a_Color;
                                 out.v_Position = uniforms.u_Matrix * vec4<f32>(in.a_Pos.xy, 0.0, 1.0);
                                 return out;
                             }

                             struct FragmentOutput {
                                 @location(0) o_Target: vec4<f32>
                             };

                             @group(0)
                             @binding(1)
                             var u_Sampler: sampler;
                             @group(1)
                             @binding(2)
                             var u_Texture: texture_2d<f32>;

                             @fragment
                             fn fs_main(in: VertexOutput) -> FragmentOutput {
                                 let color = in.v_Color;

                                 return FragmentOutput(color * textureSample(u_Texture, u_Sampler, in.v_UV));
                             }
                             """u8;

            _shaderModule = device.CreateShaderModuleWGSL(shaderCode,
                new Safe.ShaderModuleCompilationHint[]
                {
                    new Safe.ShaderModuleCompilationHint("vs_main", pipelineLayout),
                    new Safe.ShaderModuleCompilationHint("fs_main", pipelineLayout),
                });

            _pipeline = _device.CreateRenderPipeline(
                pipelineLayout,
                vertex: new Safe.VertexState
                {
                    Module = _shaderModule,
                    EntryPoint = "vs_main",
                    Buffers = vertexLayouts,
                    Constants = Array.Empty<(string, double)>()
                },
                primitive: new Safe.PrimitiveState()
                {
                    Topology = PrimitiveTopology.TriangleList,
                    StripIndexFormat = IndexFormat.Undefined,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.None
                },
                depthStencil: null,
                multisample: new MultisampleState
                {
                    Count = 1,
                    Mask = uint.MaxValue,
                    AlphaToCoverageEnabled = false
                },
                fragment: new Safe.FragmentState
                {
                    Module = _shaderModule,
                    EntryPoint = "fs_main",
                    Targets = new Safe.ColorTargetState[]
                    {
                        new Safe.ColorTargetState(colorOutputFormat, (
                                new BlendComponent(BlendOperation.Add, BlendFactor.SrcAlpha,
                                    BlendFactor.OneMinusSrcAlpha),
                                new BlendComponent(BlendOperation.Add, BlendFactor.SrcAlpha, BlendFactor.DstAlpha)),
                            ColorWriteMask.All)
                    },
                    Constants = Array.Empty<(string, double)>()
                },
                label: "ImGui.NET Pipeline"
            );

            _mainBindGroup = device.CreateBindGroup(_layout,
                new BindGroupEntry[]
                {
                    Buffer(0, _projMatrixBuffer, 0, 64),
                    Sampler(1, pointSampler)
                },
                label: "ImGui.NET Main Resource Set");

            RecreateFontDeviceTexture(device);
        }

        /// <summary>
        /// Creates the <see cref="BindGroup"/> necessary for using <paramref name="textureView"/> in <see cref="Render(RenderPassEncoderPtr)"/>.
        /// <para>If this method wasn't called the <see cref="BindGroup"/> will be created just in time</para>
        /// </summary>
        public unsafe IntPtr CreateTextureBindGroup(TextureViewPtr textureView)
        {
            if (!_bindGroupsByView.TryGetValue(textureView, out BindGroupPtr bindGroup))
            {
                bindGroup = CreateTextureBindGroupInternal(textureView);
            }

            return bindGroup.GetIntPtr();
        }

        private unsafe BindGroupPtr CreateTextureBindGroupInternal(TextureViewPtr textureView)
        {
            var entry = Texture(2, textureView);
            var bindGroup = _device.CreateBindGroup(_textureLayout,
                new ReadOnlySpan<BindGroupEntry>(&entry, 1));

            _bindGroupsByView.Add(textureView, bindGroup);

            return bindGroup;
        }

        public void RemoveTextureBindGroup(TextureViewPtr textureView)
        {
            if (_bindGroupsByView.TryGetValue(textureView, out BindGroupPtr bindGroup))
            {
                _bindGroupsByView.Remove(textureView);
                bindGroup.Release();
            }
        }

        public void ClearCachedImageResources()
        {
            foreach (BindGroupPtr bindGroup in _bindGroupsByView.Values)
            {
                bindGroup.Release();
            }

            _bindGroupsByView.Clear();
        }

        /// <summary>
        /// Recreates the device texture used to render text.
        /// </summary>
        public unsafe void RecreateFontDeviceTexture() => RecreateFontDeviceTexture(_device);

        /// <summary>
        /// Recreates the device texture used to render text.
        /// </summary>
        public unsafe void RecreateFontDeviceTexture(DevicePtr device)
        {
            var io = Hexa.NET.ImGui.ImGui.GetIO();
            //io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out int bytesPerPixel);
            if ((io.Fonts.TexData.Status & ImTextureStatus.WantCreate) == 0)
            {
                return;
            }

            var pixels = io.Fonts.TexData.Pixels;
            var width = io.Fonts.TexData.Width;
            var height = io.Fonts.TexData.Height;
            var bytesPerPixel = io.Fonts.TexData.BytesPerPixel;


            // Store our identifier
            io.Fonts.TexData.SetTexID(new ImTextureID(_fontAtlasID));
            io.Fonts.TexData.SetStatus(ImTextureStatus.Ok);

            _fontTexture?.Destroy();
            _fontTexture = device.CreateTexture(
                TextureUsage.TextureBinding | TextureUsage.CopyDst, TextureDimension.Dimension2D, new Extent3D(
                    (uint)width,
                    (uint)height,
                    depthOrArrayLayers: 1), TextureFormat.Rgba8Unorm,
                mipLevelCount: 1,
                sampleCount: 1,
                viewFormats: new TextureFormat[]
                {
                    TextureFormat.Rgba8Unorm
                },
                label: "ImGui.NET Font Texture");

            device.GetQueue().WriteTexture<byte>(
                new Safe.ImageCopyTexture
                {
                    Aspect = TextureAspect.All,
                    MipLevel = 0,
                    Origin = new(0, 0, 0),
                    Texture = _fontTexture.Value
                },
                data: new Span<byte>(pixels,
                    (int)(bytesPerPixel * width * height)),
                dataLayout: new TextureDataLayout
                {
                    BytesPerRow = (uint)(bytesPerPixel * width),
                    RowsPerImage = (uint)height,
                    Offset = 0
                },
                writeSize: new Extent3D
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    DepthOrArrayLayers = 1
                });

            var fontTextureView = _fontTexture.Value.CreateView(TextureFormat.Rgba8Unorm,
                TextureViewDimension.Dimension2D, TextureAspect.All,
                baseMipLevel: 0, mipLevelCount: 1, baseArrayLayer: 0, arrayLayerCount: 1,
                label: "ImGui.NET Font Texture - View");

            _fontTextureResourceSet?.Release();
            _fontTextureResourceSet = device.CreateBindGroup(_textureLayout,
                new BindGroupEntry[]
                {
                    Texture(2, fontTextureView)
                },
                label: "ImGui.NET Font Texture Resource Set");

            //io.Fonts.Clear();
        }

        private unsafe void RenderImDrawData(ImDrawDataPtr draw_data, RenderPassEncoderPtr pass)
        {
            uint vertexOffsetInVertices = 0;
            uint indexOffsetInElements = 0;

            if (draw_data.CmdListsCount == 0)
            {
                return;
            }

            uint totalVBSize = (uint)(draw_data.TotalVtxCount * sizeof(ImDrawVert));
            if (totalVBSize > _vertexBuffer.Size)
            {
                _vertexBuffer.Buffer.Destroy();
                ulong size = NextValidBufferSize((ulong)(totalVBSize * 1.5f));
                _vertexBuffer = new(
                    _device.CreateBuffer(_vertexBuffer.Buffer.GetUsage(), size, label: "ImGui.NET Vertex Buffer"),
                    Offset: 0, size);
            }

            uint totalIBSize = (uint)(draw_data.TotalIdxCount * sizeof(ushort));
            if (totalIBSize > _indexBuffer.Size)
            {
                _indexBuffer.Buffer.Destroy();
                ulong size = NextValidBufferSize((ulong)(totalIBSize * 1.5f));
                _indexBuffer = new(
                    _device.CreateBuffer(_indexBuffer.Buffer.GetUsage(), size, label: "ImGui.NET Index Buffer"),
                    Offset: 0, size);
            }

            var queue = _device.GetQueue();


            var vertexData = new ImDrawVert[draw_data.TotalVtxCount];
            var indexData = new ushort[draw_data.TotalIdxCount];

            for (int i = 0; i < draw_data.CmdListsCount; i++)
            {
                ImDrawListPtr cmd_list = draw_data.CmdLists[i];

                new ReadOnlySpan<ImDrawVert>((void*)cmd_list.VtxBuffer.Data, cmd_list.VtxBuffer.Size)
                    .CopyTo(new Span<ImDrawVert>(vertexData, (int)vertexOffsetInVertices, cmd_list.VtxBuffer.Size));

                new ReadOnlySpan<ushort>((void*)cmd_list.IdxBuffer.Data, cmd_list.IdxBuffer.Size)
                    .CopyTo(new Span<ushort>(indexData, (int)indexOffsetInElements, cmd_list.IdxBuffer.Size));

                vertexOffsetInVertices += (uint)cmd_list.VtxBuffer.Size;
                indexOffsetInElements += (uint)cmd_list.IdxBuffer.Size;
            }


            queue.WriteBufferAligned<ImDrawVert>(
                _vertexBuffer.Buffer,
                _vertexBuffer.Offset,
                data: vertexData
            );

            queue.WriteBufferAligned<ushort>(
                _indexBuffer.Buffer,
                _indexBuffer.Offset,
                data: indexData
            );

            for (int i = 0; i < draw_data.Textures.Size; i++)
            {
                var tex = draw_data.Textures[i];
                if ((tex.Status & ImTextureStatus.WantUpdates) == ImTextureStatus.WantUpdates)
                {
                    if (tex.TexID == _fontAtlasID)
                    {
                        queue.WriteTexture(
                            new Safe.ImageCopyTexture
                            {
                                Aspect = TextureAspect.All,
                                MipLevel = 0,
                                Origin = new(0, 0, 0),
                                Texture = _fontTexture.Value
                            },
                            data: new Span<byte>(tex.Pixels,
                                (int)(tex.BytesPerPixel * tex.Width * tex.Height)),
                            dataLayout: new TextureDataLayout
                            {
                                BytesPerRow = (uint)(tex.BytesPerPixel * tex.Width),
                                RowsPerImage = (uint)tex.Height,
                                Offset = 0
                            },
                            writeSize: new Extent3D
                            {
                                Width = (uint)tex.Width,
                                Height = (uint)tex.Height,
                                DepthOrArrayLayers = 1
                            });
                        tex.SetStatus(ImTextureStatus.Ok);
                    }
                }
            }

            // Setup orthographic projection matrix into our constant buffer
            {
                var io = Hexa.NET.ImGui.ImGui.GetIO();

                Matrix4x4 mvp = Matrix4x4.CreateOrthographicOffCenter(
                    0f,
                    io.DisplaySize.X,
                    io.DisplaySize.Y,
                    0.0f,
                    -1.0f,
                    1.0f);

                queue.WriteBuffer(_projMatrixBuffer, 0, new ReadOnlySpan<Matrix4x4>(&mvp, 1));
            }

            pass.SetVertexBuffer(0, _vertexBuffer.Buffer, _vertexBuffer.Offset, _vertexBuffer.Size);
            pass.SetIndexBuffer(_indexBuffer.Buffer, IndexFormat.Uint16, _indexBuffer.Offset, _indexBuffer.Size);
            pass.SetPipeline(_pipeline);
            pass.SetBindGroup(0, _mainBindGroup, dynamicOffsets: null);

            draw_data.ScaleClipRects(Hexa.NET.ImGui.ImGui.GetIO().DisplayFramebufferScale);

            // Render command lists
            int vtx_offset = 0;
            int idx_offset = 0;
            for (int n = 0; n < draw_data.CmdListsCount; n++)
            {
                ImDrawListPtr cmd_list = draw_data.CmdLists[n];
                for (int cmd_i = 0; cmd_i < cmd_list.CmdBuffer.Size; cmd_i++)
                {
                    var pcmd = cmd_list.CmdBuffer[cmd_i];
                    var textureId = pcmd.TexRef.TexData is not null ? pcmd.TexRef.TexData->TexID : pcmd.TexRef.TexID;
                    if (pcmd.UserCallback is not null)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        if (textureId != IntPtr.Zero)
                        {
                            if (textureId == _fontAtlasID)
                            {
                                pass.SetBindGroup(1, _fontTextureResourceSet!.Value, dynamicOffsets: null);
                            }
                            else
                            {
                                var textureView = new TextureViewPtr(_device.GetAPI(), (TextureView*)textureId);

                                if (!_bindGroupsByView.TryGetValue(textureView, out BindGroupPtr bindGroup))
                                    bindGroup = CreateTextureBindGroupInternal(textureView);

                                pass.SetBindGroup(1, bindGroup, dynamicOffsets: null);
                            }
                        }

                        var x1 = 1920;
                        var scissorRectWidth = Math.Min((uint)x1, (uint)pcmd.ClipRect.Z) -
                                               (uint)pcmd.ClipRect.X;
                        var y1 = 1080;
                        var scissorRectHeight = Math.Min((uint)y1, (uint)pcmd.ClipRect.W) -
                                                (uint)pcmd.ClipRect.Y;

                        if (scissorRectWidth * scissorRectHeight == 0)
                            continue;

                        pass.SetScissorRect(
                            Math.Max(0, (uint)pcmd.ClipRect.X),
                            Math.Max(0, (uint)pcmd.ClipRect.Y),
                            scissorRectWidth, scissorRectHeight
                        );

                        pass.DrawIndexed(pcmd.ElemCount, 1, pcmd.IdxOffset + (uint)idx_offset,
                            (int)(pcmd.VtxOffset + vtx_offset), 0);
                    }
                }

                idx_offset += cmd_list.IdxBuffer.Size;
                vtx_offset += cmd_list.VtxBuffer.Size;
            }
        }

        /// <summary>
        /// Frees all graphics resources used by the renderer.
        /// </summary>
        public void Dispose()
        {
            _vertexBuffer.Buffer.Destroy();
            _indexBuffer.Buffer.Destroy();
            _projMatrixBuffer.Destroy();
            _fontTexture?.Destroy();
            _shaderModule.Release();
            _layout.Release();
            _textureLayout.Release();
            _pipeline.Release();
            _mainBindGroup.Release();
            _fontTextureResourceSet?.Release();

            foreach (BindGroupPtr bindGroup in _bindGroupsByView.Values)
            {
                bindGroup.Release();
            }
        }
    }
}