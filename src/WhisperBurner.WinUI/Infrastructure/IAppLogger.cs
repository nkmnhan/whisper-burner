namespace WhisperBurner.WinUI.Infrastructure;

public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}
