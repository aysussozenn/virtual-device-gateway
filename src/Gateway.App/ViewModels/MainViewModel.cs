using System.Collections.ObjectModel;
using Gateway.Ethernet;

namespace Gateway.App.ViewModels;

/// <summary>
/// Root view model. The app is built around the interactive <see cref="PeerConsoleViewModel"/>
/// (drive ICD-generated peers); <see cref="ConformanceViewModel"/> is the secondary automated
/// verification surface. The capture-adapter list is owned here and shared with both.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    public ObservableCollection<AdapterItem> Adapters { get; } = new();

    public RelayCommand RefreshAdaptersCommand { get; }

    /// <summary>Automated PASS/FAIL verification: point it at a topology + scenario and run.</summary>
    public ConformanceViewModel Conformance { get; } = new();

    /// <summary>Interactive peer emulation generated from each peer's ICD.</summary>
    public PeerConsoleViewModel PeerConsole { get; }

    public MainViewModel()
    {
        RefreshAdaptersCommand = new RelayCommand(RefreshAdapters);
        RefreshAdapters();
        PeerConsole = new PeerConsoleViewModel(Adapters);
    }

    private void RefreshAdapters()
    {
        Adapters.Clear();
        try
        {
            foreach (var d in AdapterDiscovery.List())
                Adapters.Add(new AdapterItem(d));
        }
        catch
        {
            /* adapter discovery needs Npcap; absence is fine in demo mode */
        }
        PeerConsole?.SelectDefaultAdapter();
    }
}
