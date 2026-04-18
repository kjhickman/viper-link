using ViperLink.App.Domain;

namespace ViperLink.App.Application;

public interface IMousePowerReader
{
    MousePowerSnapshot Probe();
}
