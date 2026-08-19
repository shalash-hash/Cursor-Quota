namespace Quota.Services;

public class CursorAuthException : Exception
{
    public CursorAuthException()
        : base("Не удалось получить авторизацию Cursor. Убедитесь, что Cursor запущен и выполнен вход в аккаунт.")
    {
    }

    public CursorAuthException(string message) : base(message)
    {
    }
}
