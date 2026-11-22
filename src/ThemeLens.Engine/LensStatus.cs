// SPDX-License-Identifier: GPL-3.0-or-later
namespace ThemeLens.Engine;

/// <summary>The states of figure 8.</summary>
public enum LensState
{
    Off,

    /// <summary>The lens shows the plain page colour. Always passed through on the way to <see cref="Live"/>.</summary>
    HoldingBackground,

    /// <summary>The lens shows the recoloured picture and passes all input through.</summary>
    Live,

    /// <summary>As <see cref="Live"/>, but the lens body takes input so it can be moved and resized.</summary>
    Edit,
}

/// <summary>Why the lens is not showing a picture, when it is not.</summary>
public enum HoldReason
{
    None,
    Starting,
    CaptureFault,
    DisplayChanged,
    Stalled,
}

/// <summary>What the status pill in the control window says (figure 5).</summary>
public sealed record LensStatus
{
    public static readonly LensStatus Off = new() { State = LensState.Off };

    public required LensState State { get; init; }

    public HoldReason HoldReason { get; init; }

    /// <summary>True when the lens runs slower than the Standard budget (FR-25). Nothing is switched off because of it.</summary>
    public bool ReducedSpeed { get; init; }

    /// <summary>The option that costs most while <see cref="ReducedSpeed"/> is true, if one can be named.</summary>
    public SlowOption SlowOption { get; init; }

    /// <summary>Typical delay from a change on screen to the lens showing it.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>Set when capture is blocked or unsupported: the lens is off and this explains why (FR-20).</summary>
    public string? CaptureProblem { get; init; }

    /// <summary>
    /// The status in the words of the specification. The app shows localised text from its resources; this
    /// string is for logs and for the executable specifications.
    /// </summary>
    public string Text => State switch
    {
        LensState.Off => CaptureProblem is null ? "Off" : "Capture unavailable",
        LensState.HoldingBackground => "Holding background",
        _ => ReducedSpeed ? "Live, reduced speed" : "Live",
    };
}

public enum SlowOption
{
    None,
    StrongSharpen,
    LocalContrast,
    CpuRendering,
}
