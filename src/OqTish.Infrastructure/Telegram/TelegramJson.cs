using System.Text.Json;
using System.Text.Json.Nodes;
using OqTish.Application.Notifications.Telegram;

namespace OqTish.Infrastructure.Telegram;

/// <summary>Bot API JSON (snake_case) ↔ the Application's Telegram records.</summary>
public static class TelegramJson
{
    /// <summary>Parses one Bot API <c>Update</c> object; returns null when it is not an object or has no <c>update_id</c>.</summary>
    public static TelegramUpdate? ParseUpdate(JsonElement update)
    {
        if (update.ValueKind != JsonValueKind.Object || !update.TryGetProperty("update_id", out var idElement) || !idElement.TryGetInt64(out var updateId))
        {
            return null;
        }

        TelegramIncomingMessage? message = null;
        if (update.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object
            && m.TryGetProperty("chat", out var chat) && chat.ValueKind == JsonValueKind.Object && chat.TryGetProperty("id", out var chatIdElement) && chatIdElement.TryGetInt64(out var chatId))
        {
            long? fromId = null;
            var isBot = false;
            string? language = null;
            if (m.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Object)
            {
                if (from.TryGetProperty("id", out var fid) && fid.TryGetInt64(out var f))
                {
                    fromId = f;
                }

                isBot = from.TryGetProperty("is_bot", out var b) && b.ValueKind == JsonValueKind.True;
                language = from.TryGetProperty("language_code", out var lc) && lc.ValueKind == JsonValueKind.String ? lc.GetString() : null;
            }

            message = new TelegramIncomingMessage(
                m.TryGetProperty("message_id", out var mid) && mid.TryGetInt64(out var messageId) ? messageId : 0,
                chatId,
                chat.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString() ?? string.Empty : string.Empty,
                fromId,
                isBot,
                language,
                m.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null);
        }

        return new TelegramUpdate(updateId, message);
    }

    public static JsonObject SendMessageBody(TelegramOutgoingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var body = new JsonObject
        {
            ["chat_id"] = message.ChatId,
            ["text"] = message.Text,
            ["link_preview_options"] = new JsonObject { ["is_disabled"] = true },
        };
        if (message.Buttons is { Count: > 0 } rows)
        {
            body["reply_markup"] = new JsonObject { ["inline_keyboard"] = Keyboard(rows) };
        }

        return body;
    }

    public static JsonArray Keyboard(IReadOnlyList<IReadOnlyList<TelegramButton>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var keyboard = new JsonArray();
        foreach (var row in rows)
        {
            var buttons = new JsonArray();
            foreach (var button in row)
            {
                var node = new JsonObject { ["text"] = button.Text };
                if (button.WebAppUrl is { } webApp)
                {
                    node["web_app"] = new JsonObject { ["url"] = webApp };
                }
                else if (button.Url is { } url)
                {
                    node["url"] = url;
                }

                buttons.Add(node);
            }

            keyboard.Add(buttons);
        }

        return keyboard;
    }
}
