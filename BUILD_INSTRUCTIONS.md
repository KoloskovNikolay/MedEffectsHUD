# Инструкция по сборке MedEffectsHUD

## Требования

- .NET SDK (с поддержкой `net472`)
- Python 3.x (для правильной упаковки ZIP)
- Установленный SPT 4.0.x (путь указан в `.csproj`)

---

## Структура проекта

```
MedEffectsHUD/
├── MedEffectsHUD.csproj          # Файл проекта
├── MedEffectsHUDPlugin.cs        # Основной код мода
├── icons/                         # Иконки для эффектов
│   ├── positive/                 # Иконки позитивных эффектов
│   ├── negative/                 # Иконки негативных эффектов
│   └── *.png                     # Общие иконки (32×32 PNG)
├── dist/                          # Папка для stage-сборки (создаётся автоматически)
│   └── BepInEx/
│       └── plugins/
│           └── MedEffectsHUD/
│               ├── MedEffectsHUD.dll
│               └── icons/
└── bin/Release/                  # Выход компиляции
    └── MedEffectsHUD.dll
```

---

## Пошаговая сборка релиза

### Шаг 1: Обновление версии

Измените версию в следующих файлах:

1. **MedEffectsHUD.csproj** — строка `<Version>`:
   ```xml
   <Version>1.1.3</Version>
   ```

2. **README.md** — строка с именем архива:
   ```markdown
   1. Download the latest release archive (`MedEffectsHUD-v1.1.3.zip`).
   ```

3. **Создайте PATCH_NOTES_vX.X.X.md** — опишите изменения релиза.

---

### Шаг 2: Компиляция DLL

```bash
cd /e/CustomMods/MedEffectsHUD
dotnet build -c Release
```

Результат: `bin/Release/MedEffectsHUD.dll`

---

### Шаг 3: Подготовка staging-папки

```bash
cd /e/CustomMods/MedEffectsHUD

# Очистить и создать заново
rm -rf dist/BepInEx
mkdir -p dist/BepInEx/plugins/MedEffectsHUD/icons

# Скопировать DLL и иконки
cp bin/Release/MedEffectsHUD.dll dist/BepInEx/plugins/MedEffectsHUD/
cp -r icons/* dist/BepInEx/plugins/MedEffectsHUD/icons/
```

Структура `dist/`:
```
dist/
└── BepInEx/
    └── plugins/
        └── MedEffectsHUD/
            ├── MedEffectsHUD.dll
            └── icons/
                ├── *.png
                ├── positive/
                └── negative/
```

---

### Шаг 4: Упаковка архива

**ВАЖНО:** Используйте Python для упаковки, чтобы пути в ZIP были с прямыми слешами `/`, а не `\`.  
Dragon Den и другие установщики **отклоняют** архивы с обратными слешами Windows.

#### Клиентский архив (MedEffectsHUD-vX.X.X.zip)

```bash
python - <<'PY'
import os
import zipfile

base_dir = r"E:\CustomMods\MedEffectsHUD\dist"
zip_path = r"E:\CustomMods\MedEffectsHUD\MedEffectsHUD-v1.1.3.zip"

with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
    for root, dirs, files in os.walk(base_dir):
        for name in files:
            full_path = os.path.join(root, name)
            rel_path = os.path.relpath(full_path, base_dir)
            arcname = rel_path.replace(os.sep, "/")
            zf.write(full_path, arcname)
PY
```

**Результат:** `MedEffectsHUD-v1.1.3.zip` с правильной структурой для распаковки в корень игры.

Содержимое архива:
```
BepInEx/plugins/MedEffectsHUD/MedEffectsHUD.dll
BepInEx/plugins/MedEffectsHUD/icons/Antidote.png
BepInEx/plugins/MedEffectsHUD/icons/positive/...
BepInEx/plugins/MedEffectsHUD/icons/negative/...
```

#### Архив для Dragon Den (com.koloskovnick.medeffectshud_X.X.X.zip)

**То же содержимое, другое имя файла:**

```bash
python - <<'PY'
import os
import zipfile

base_dir = r"E:\CustomMods\MedEffectsHUD\dist"
zip_path = r"E:\CustomMods\MedEffectsHUD\com.koloskovnick.medeffectshud_1.1.3.zip"

with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
    for root, dirs, files in os.walk(base_dir):
        for name in files:
            full_path = os.path.join(root, name)
            rel_path = os.path.relpath(full_path, base_dir)
            arcname = rel_path.replace(os.sep, "/")
            zf.write(full_path, arcname)
PY
```

---

### Шаг 5: Проверка архива

```bash
unzip -l MedEffectsHUD-v1.1.3.zip | head -n 20
```

**Правильный вывод:**
```
Archive:  MedEffectsHUD-v1.1.3.zip
  Length      Date    Time    Name
---------  ---------- -----   ----
    57344  2026-02-18 19:59   BepInEx/plugins/MedEffectsHUD/MedEffectsHUD.dll
     2528  2026-02-18 19:59   BepInEx/plugins/MedEffectsHUD/icons/Antidote.png
     ...
```

**Ошибочный вывод (НЕ допускайте):**
```
Archive:  bad.zip
  Length      Date    Time    Name
---------  ---------- -----   ----
        0  2026-02-18 19:42   MedEffectsHUD-v1.1.2\BepInEx\          ❌ обратные слеши
```

---

### Шаг 6: Коммит и push

```bash
cd /e/CustomMods/MedEffectsHUD

# Добавить все изменения
git add .

# Создать коммит
git commit -m "Release 1.1.3"

# Запушить
git push
```

---

## Типичные ошибки и решения

### Ошибка: Dragon Den — "Top level check failed"

**Причина:** Пути в ZIP записаны с обратными слешами `\` или есть лишняя корневая папка.

**Решение:**
- Используйте Python для упаковки (см. Шаг 4).
- Убедитесь, что архив начинается с `BepInEx/` без промежуточной папки.

**Неправильная структура:**
```
MedEffectsHUD-v1.1.2/              ❌ лишняя корневая папка
└── BepInEx\plugins\...            ❌ обратные слеши
```

**Правильная структура:**
```
BepInEx/plugins/MedEffectsHUD/...  ✅ прямые слеши, начинается с BepInEx/
```

---

### Ошибка: PowerShell Compress-Archive создаёт неправильные пути

**Не используйте PowerShell Compress-Archive** — он всегда создаёт пути с `\`.

**Используйте:**
- Python `zipfile` (см. выше)
- Bash `tar -a` (если доступен)
- 7-Zip CLI с опцией `-r`

---

## Итоговый чеклист релиза

- [ ] Обновлена версия в `.csproj`
- [ ] Обновлена версия в `README.md`
- [ ] Создан `PATCH_NOTES_vX.X.X.md`
- [ ] Выполнена компиляция: `dotnet build -c Release`
- [ ] Создана staging-папка `dist/BepInEx/...`
- [ ] Упакован клиентский архив `MedEffectsHUD-vX.X.X.zip` (Python)
- [ ] Упакован архив для Dragon Den `com.koloskovnick.medeffectshud_X.X.X.zip` (Python)
- [ ] Проверено содержимое архива: прямые слеши `/`, начинается с `BepInEx/`
- [ ] Коммит и push в Git
- [ ] (Опционально) Скопирован архив в `E:\DragonDen\data\downloads\`

---

## Быстрый скрипт для сборки

Сохраните как `build_release.sh`:

```bash
#!/bin/bash
set -e

VERSION="1.1.3"
BASE_DIR="/e/CustomMods/MedEffectsHUD"

cd "$BASE_DIR"

echo "Building..."
dotnet build -c Release

echo "Staging..."
rm -rf dist/BepInEx
mkdir -p dist/BepInEx/plugins/MedEffectsHUD/icons
cp bin/Release/MedEffectsHUD.dll dist/BepInEx/plugins/MedEffectsHUD/
cp -r icons/* dist/BepInEx/plugins/MedEffectsHUD/icons/

echo "Packaging..."
python - <<PY
import os, zipfile

base = r"$BASE_DIR\dist"
for name in ["MedEffectsHUD-v$VERSION.zip", "com.koloskovnick.medeffectshud_$VERSION.zip"]:
    with zipfile.ZipFile(os.path.join(r"$BASE_DIR", name), "w", zipfile.ZIP_DEFLATED) as zf:
        for root, dirs, files in os.walk(base):
            for file in files:
                full = os.path.join(root, file)
                rel = os.path.relpath(full, base).replace(os.sep, "/")
                zf.write(full, rel)
PY

echo "Done! Check: MedEffectsHUD-v$VERSION.zip"
unzip -l "MedEffectsHUD-v$VERSION.zip" | head -n 15
```

Запуск:
```bash
bash build_release.sh
```

---

## Контакты и ссылки

- **Репозиторий:** `E:\CustomMods\MedEffectsHUD`
- **Dragon Den:** `E:\DragonDen\data\downloads`
- **Игра (SPT):** Указана в `.csproj` как `<GameDir>`
