namespace Sirius.Engine.Layout;

/// <summary>Built-in pages served under the sirius:// scheme.</summary>
public static class InternalPages
{
    public const string Version = "0.0.1";

    public static string Home() => $$"""
        <!DOCTYPE html>
        <html lang="ru">
        <head>
          <meta charset="utf-8">
          <title>Sirius — новая вкладка</title>
          <style>
            body { color: #20242e; }
            h1 { color: #4f46e5; }
            .accent { color: #4f46e5; }
            .muted { color: #6b7280; }
            blockquote { color: #4b5563; }
          </style>
        </head>
        <body>
          <h1>Sirius {{Version}}</h1>
          <p>Лёгкий браузер с собственным движком. Введите адрес сайта в строку
          сверху — или просто напишите запрос, и Sirius откроет результаты
          <a href="https://duckduckgo.com/">DuckDuckGo</a>.</p>
          <hr>
          <h2>С чего начать</h2>
          <ul>
            <li><a href="https://html.duckduckgo.com/html/?q=Sirius+browser+engine">Поиск в DuckDuckGo</a> — работает без JavaScript.</li>
            <li><a href="https://example.com/">example.com</a> — минимальная тестовая страница.</li>
            <li><a href="https://ru.wikipedia.org/wiki/Браузер">Википедия: Браузер</a> — длинная статья для проверки вёрстки.</li>
            <li><a href="https://github.com/">GitHub</a> — ссылки и списки.</li>
          </ul>
          <h2>Горячие клавиши</h2>
          <ul>
            <li><b>Ctrl+L</b> — перейти к адресной строке.</li>
            <li><b>Ctrl+R</b> или <b>F5</b> — обновить страницу.</li>
            <li><b>Alt+←</b> / <b>Alt+→</b> — назад и вперёд.</li>
            <li><b>Ctrl+F</b> — поиск по странице.</li>
          </ul>
          <blockquote>Sirius 0.0.1 — текстовый рендеринг HTML и CSS. JavaScript пока не поддерживается.</blockquote>
          <p class="muted">Движок: C# · Интерфейс: Python/Tkinter</p>
        </body>
        </html>
        """;

    public static string About() => $$"""
        <!DOCTYPE html>
        <html lang="ru">
        <head>
          <meta charset="utf-8">
          <title>О Sirius</title>
        </head>
        <body>
          <h1>О Sirius</h1>
          <p><b>Версия:</b> {{Version}}</p>
          <p><b>Движок:</b> Sirius.Engine (C#, .NET 8) — сеть, HTML-парсер, CSS-каскад, layout.</p>
          <p><b>Интерфейс:</b> Python 3 + Tkinter.</p>
          <p><b>Поиск:</b> <a href="https://duckduckgo.com/">DuckDuckGo</a> через HTML-эндпоинт.</p>
          <h2>Что уже умеет</h2>
          <ul>
            <li>Загружать HTTP/HTTPS-страницы, следовать редиректам, хранить cookies.</li>
            <li>Разбирать HTML, применять CSS и строить читаемый текстовый layout.</li>
            <li>Работать с историей: назад, вперёд, обновить.</li>
            <li>Искать в DuckDuckGo прямо из адресной строки.</li>
          </ul>
          <p>Следующие версии: вкладки, кэш ресурсов, загрузка изображений и CSS-файлов.</p>
        </body>
        </html>
        """;

    public static string Blank() => """
        <!DOCTYPE html>
        <html lang="ru"><head><meta charset="utf-8"><title>Новая вкладка</title></head>
        <body><p style="color:#6b7280">Пустая страница.</p></body></html>
        """;

    public static string Error(string title, string message, string? url, string kind) => $$"""
        <!DOCTYPE html>
        <html lang="ru">
        <head>
          <meta charset="utf-8">
          <title>{{Escape(title)}}</title>
          <style>
            h1 { color: #b91c1c; }
            .kind { color: #6b7280; }
            .url { color: #4f46e5; }
          </style>
        </head>
        <body>
          <h1>{{Escape(title)}}</h1>
          <p>{{Escape(message)}}</p>
          {{(string.IsNullOrWhiteSpace(url) ? "" : $"<p class=\"url\">{Escape(url)}</p>")}}
          <p class="kind">Код ошибки: {{Escape(kind)}}</p>
          <hr>
          <h2>Что можно сделать</h2>
          <ul>
            <li>Проверить адрес и попробовать снова.</li>
            <li>Нажать «Обновить» на панели сверху.</li>
            <li>Вернуться на <a href="sirius://home">домашнюю страницу</a>.</li>
          </ul>
        </body>
        </html>
        """;

    private static string Escape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
