using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using Framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace Apollo.Chat;

/// <summary>
/// Chat send via signature-scanned ProcessChatBox. Ported from XivCommon.Functions.Chat.
/// </summary>
public class Chat {
    private static class Signatures {
        internal const string SendChat = "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B F2 48 8B F9 45 84 C9";
        internal const string SanitiseString = "E8 ?? ?? ?? ?? 48 8D 4C 24 ?? 0F B6 F8 E8 ?? ?? ?? ?? 48 8D 4D ?? 44 0F B6 F7";
    }

    private delegate void ProcessChatBoxDelegate(IntPtr uiModule, IntPtr message, IntPtr unused, byte a4);

    private ProcessChatBoxDelegate? ProcessChatBox { get; }

    private readonly unsafe delegate* unmanaged<Utf8String*, int, IntPtr, void> _sanitiseString = null!;

    public Chat(ISigScanner scanner) {
        if (scanner.TryScanText(Signatures.SendChat, out var processChatBoxPtr)) {
            this.ProcessChatBox = Marshal.GetDelegateForFunctionPointer<ProcessChatBoxDelegate>(processChatBoxPtr);
        }

        unsafe {
            if (scanner.TryScanText(Signatures.SanitiseString, out var sanitisePtr)) {
                this._sanitiseString = (delegate* unmanaged<Utf8String*, int, IntPtr, void>)sanitisePtr;
            }
        }
    }

    public unsafe void SendMessageUnsafe(byte[] message) {
        if (this.ProcessChatBox == null) {
            throw new InvalidOperationException("Could not find signature for chat sending");
        }

        var uiModule = (IntPtr)Framework.Instance()->UIModule;

        using var payload = new ChatPayload(message);
        var mem1 = Marshal.AllocHGlobal(400);
        Marshal.StructureToPtr(payload, mem1, false);

        this.ProcessChatBox(uiModule, mem1, IntPtr.Zero, 0);

        Marshal.FreeHGlobal(mem1);
    }

    public void SendMessage(string message) {
        var bytes = Encoding.UTF8.GetBytes(message);
        if (bytes.Length == 0) {
            throw new ArgumentException("message is empty", nameof(message));
        }

        if (bytes.Length > 500) {
            throw new ArgumentException("message is longer than 500 bytes", nameof(message));
        }

        if (message.Length != this.SanitiseText(message).Length) {
            throw new ArgumentException("message contained invalid characters", nameof(message));
        }

        this.SendMessageUnsafe(bytes);
    }

    public unsafe string SanitiseText(string text) {
        var uText = Utf8String.FromString(text);

        uText->SanitizeString((AllowedEntities)0x27F);
        var sanitised = uText->ToString();

        uText->Dtor(true);

        return sanitised;
    }

    [StructLayout(LayoutKind.Explicit)]
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    private readonly struct ChatPayload : IDisposable {
        [FieldOffset(0)]
        private readonly IntPtr textPtr;

        [FieldOffset(16)]
        private readonly ulong textLen;

        [FieldOffset(8)]
        private readonly ulong unk1;

        [FieldOffset(24)]
        private readonly ulong unk2;

        internal ChatPayload(byte[] stringBytes) {
            this.textPtr = Marshal.AllocHGlobal(stringBytes.Length + 30);
            Marshal.Copy(stringBytes, 0, this.textPtr, stringBytes.Length);
            Marshal.WriteByte(this.textPtr + stringBytes.Length, 0);

            this.textLen = (ulong)(stringBytes.Length + 1);

            this.unk1 = 64;
            this.unk2 = 0;
        }

        public void Dispose() {
            Marshal.FreeHGlobal(this.textPtr);
        }
    }
}
