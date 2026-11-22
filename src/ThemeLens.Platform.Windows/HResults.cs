// SPDX-License-Identifier: GPL-3.0-or-later
namespace ThemeLens.Platform.Windows;

/// <summary>The few HRESULT values this layer tells apart.</summary>
internal static class HResults
{
    public const int AccessDenied = unchecked((int)0x80070005);
    public const int DxgiUnsupported = unchecked((int)0x887A0004);
    public const int DxgiDeviceRemoved = unchecked((int)0x887A0005);
    public const int DxgiDeviceHung = unchecked((int)0x887A0006);
    public const int DxgiDeviceReset = unchecked((int)0x887A0007);
    public const int DxgiWasStillDrawing = unchecked((int)0x887A000A);
    public const int DxgiDriverInternalError = unchecked((int)0x887A0020);
    public const int DxgiNotCurrentlyAvailable = unchecked((int)0x887A0022);
    public const int DxgiAccessLost = unchecked((int)0x887A0026);
    public const int DxgiWaitTimeout = unchecked((int)0x887A0027);
    public const int DxgiSessionDisconnected = unchecked((int)0x887A0028);

    /// <summary>True for the results that mean the GPU device is gone, and everything made from it with it.</summary>
    public static bool IsDeviceLoss(int hresult) =>
        hresult is DxgiDeviceRemoved or DxgiDeviceHung or DxgiDeviceReset or DxgiDriverInternalError;
}
