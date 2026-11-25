using System.ComponentModel;
using FirebirdStudio.Core.Connections;
using static FirebirdStudio.Desktop.Services.StudioText;

namespace FirebirdStudio.Desktop.Views;

internal sealed class ConnectionListItem(ConnectionProfile profile) : INotifyPropertyChanged
{
    public ConnectionProfile Profile { get; private set; } = profile;
    public bool IsConnected { get; private set; }
    public string Status => T(IsConnected ? "Connected" : "Offline");
    public string AccessibleName => $"{Profile.Name}, {Status}";
    public string Endpoint
    {
        get
        {
            if (Profile.ServerType == ConnectionServerType.Embedded)
                return T("EmbeddedDatabase");

            var host = Profile.Host ?? string.Empty;
            if (host.Contains(':') && !host.StartsWith('['))
                host = $"[{host}]";
            return $"{host}:{Profile.Port}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateProfile(ConnectionProfile updatedProfile)
    {
        Profile = updatedProfile;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Profile)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Endpoint)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
    }

    public void UpdateConnectionState(bool isConnected)
    {
        if (IsConnected == isConnected)
            return;

        IsConnected = isConnected;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
    }
}
