using System.Device.Gpio;
using System.Diagnostics;
using Iot.Device.Pwm;

namespace HP.Wand.GPIO;

public class GpioService : IDisposable
{
    private GpioController? _gpio;
    private readonly Dictionary<int, SoftwarePwmChannel> _pwmChannels = [];
    private readonly HashSet<int> _openPins = [];
    private bool _disposed;

    // Allow running without real GPIO hardware (e.g. development on non-Pi)
    private readonly bool _simulation;

    public GpioService(bool simulation = false)
    {
        _simulation = simulation;
        if (!simulation)
        {
            try { _gpio = new GpioController(); }
            catch
            {
                Console.Error.WriteLine("Warning: GPIO not available — running in simulation mode.");
                _simulation = true;
            }
        }
    }

    public void SetDigital(int pin, int value)
    {
        if (_simulation)
        {
            Console.WriteLine($"[SIM] GPIO pin {pin} = {value}");
            return;
        }

        EnsurePinOpen(pin, PinMode.Output);
        _gpio!.Write(pin, value == 1 ? PinValue.High : PinValue.Low);
    }

    public void SetPwm(int pin, int dutyCycle)
    {
        if (_simulation)
        {
            Console.WriteLine($"[SIM] PWM pin {pin} duty={dutyCycle}");
            return;
        }

        if (!_pwmChannels.TryGetValue(pin, out var channel))
        {
            // 50 Hz, duty expressed as 0-255 fraction
            channel = new SoftwarePwmChannel(pin, frequency: 50, dutyCycle: 0.0);
            channel.Start();
            _pwmChannels[pin] = channel;
        }

        channel.DutyCycle = Math.Clamp(dutyCycle / 255.0, 0.0, 1.0);
    }

    public void PlaySound(string file)
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Sound file not found: {file}");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo("aplay", file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Sound playback failed: {ex.Message}");
        }
    }

    private void EnsurePinOpen(int pin, PinMode mode)
    {
        if (!_openPins.Contains(pin))
        {
            _gpio!.OpenPin(pin, mode);
            _openPins.Add(pin);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var ch in _pwmChannels.Values)
        {
            ch.Stop();
            ch.Dispose();
        }
        _pwmChannels.Clear();

        foreach (var pin in _openPins)
            _gpio?.ClosePin(pin);

        _gpio?.Dispose();
    }
}
