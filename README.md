# Gestor de Tareas

Gestor de tareas estilo Trello para **Android** y **Windows**, nativo y sin servidor web.
Un único código en **C# / .NET MAUI** que compila a un APK/AAB de Android y a un `.exe` de Windows.

## Funcionalidades

| Área | Detalle |
|---|---|
| Usuarios | Registro, inicio de sesión persistente (30 días, renovable), perfil, cambio de contraseña, borrado de cuenta |
| Tableros | Varios tableros por usuario, con color, descripción, progreso y conteo de vencidas |
| Tareas | Crear/editar/eliminar; columnas **Por hacer · En proceso · Completado**; arrastrar y soltar para mover y reordenar |
| Etiquetas | Etiquetas de color propias, reutilizables en todos los tableros |
| Prioridades | Alta, Media, Baja (franja de color en cada tarjeta) |
| Fechas | Vencimiento con fecha y hora, tarjetas vencidas en rojo, pantalla de **Alertas** y aviso al iniciar sesión |
| Filtros | Búsqueda de texto + prioridad + etiqueta + vencimiento (vencidas, hoy, próximos 7 días, sin fecha) |
| Comentarios | Comentarios por tarea más historial automático (creación, cambios de estado y de prioridad) |

### Interfaz por plataforma

- **Android (táctil, Material):** menú lateral (navigation drawer), pestañas por estado con una columna a la vez,
  botón flotante “+”, **deslizar** una tarjeta para pasarla a la columna anterior/siguiente y **pulsación larga** para arrastrarla.
  En tablets (≥ 760 dp) se muestran las tres columnas juntas.
- **Windows (mouse y teclado):** panel lateral fijo, tablero horizontal de tres columnas, **arrastrar y soltar** con el mouse,
  menú contextual (clic derecho) en las tarjetas y atajos de teclado:

  | Atajo | Acción |
  |---|---|
  | `Ctrl+N` | Nueva tarea / nuevo tablero |
  | `Ctrl+F` | Buscar en el tablero |
  | `Ctrl+L` | Limpiar filtros |
  | `Ctrl+E` | Editar el tablero |
  | `F5` | Actualizar |
  | `Ctrl+S` | Guardar la tarea |
  | `Ctrl+W` | Volver sin guardar |

## Arquitectura

```
Gestor de Tareas/
├─ src/TaskManager.Core/     Dominio, servicios, seguridad y persistencia (net10.0, sin UI)
│  ├─ Domain/                Entidades: User, SessionRecord, Project, TaskItem, Label, TaskLabel, Comment
│  ├─ Security/              PasswordHasher (PBKDF2), Tokens, InputSanitizer, ISecretStore
│  ├─ Data/AppDatabase.cs    SQLite cifrada con SQLCipher
│  └─ Services/              AuthService, ProjectService, TaskService, LabelService, CommentService
├─ src/TaskManager.App/      App .NET MAUI (Android + Windows), MVVM con CommunityToolkit.Mvvm
│  ├─ Views/ + Controls/     Páginas XAML y controles (tarjeta, columna)
│  ├─ ViewModels/            Lógica de presentación
│  └─ Services/              Almacén seguro por plataforma, sesión, diálogos
├─ tests/TaskManager.Core.Tests/   Tests xUnit del núcleo (corren sin emulador)
└─ build.ps1                 Tests + empaquetado de Windows y Android
```

Es una app **local-first**: cada dispositivo guarda sus datos en una base SQLite cifrada. No hay servidor,
así que no hay sincronización entre dispositivos. Como toda la lógica está en `TaskManager.Core` (sin dependencias de UI),
agregar luego una API REST central implica sumar un proyecto nuevo, sin reescribir el dominio.

## Seguridad

- **Base de datos cifrada en reposo** (SQLCipher, AES-256). La clave es aleatoria (256 bits) por instalación y se guarda
  en el almacén seguro del sistema, nunca junto a la base:
  - Android: `SecureStorage` → Android Keystore. Además `allowBackup=false` para que la base no viaje en copias de seguridad.
  - Windows: DPAPI con ámbito del usuario de Windows (`%LOCALAPPDATA%\GestorTareas\secrets.bin`).
- **Contraseñas:** PBKDF2-HMAC-SHA256 con 600.000 iteraciones y sal aleatoria; comparación en tiempo constante;
  re-hash automático si se suben las iteraciones.
- **Tokens de sesión:** 256 bits aleatorios. El token vive en el almacén seguro; en la base sólo se guarda su hash SHA-256.
  Cerrar sesión o cambiar la contraseña revoca las sesiones.
- **Fuerza bruta:** bloqueo de 5 minutos tras 5 intentos fallidos; mensajes de error que no revelan si el email existe.
- **Inyección:** todas las consultas son parametrizadas (sqlite-net); nunca se concatena SQL. Toda entrada se valida y
  sanea: normalización Unicode, eliminación de caracteres de control y de override bidireccional, límites de longitud,
  colores sólo `#RRGGBB`, formato de email y política de contraseñas.
- **Aislamiento entre usuarios:** cada operación verifica que el recurso pertenezca al usuario de la sesión; las etiquetas
  ajenas se descartan.

## Requisitos

- Windows 10/11 x64
- [.NET SDK 10](https://dotnet.microsoft.com/download) (10.0.400 o superior)
- Workloads de MAUI: `dotnet workload install maui-android maui-windows` (desde una consola con permisos de administrador)
- Para Android: JDK 17 (`winget install Microsoft.OpenJDK.17`). El Android SDK lo descarga `build.ps1` la primera vez.

## Compilar

Todo de una vez (tests, `.exe` y APK):

```powershell
./build.ps1
```

O por partes:

```powershell
./build.ps1 -Target test      # tests del núcleo
./build.ps1 -Target windows   # .exe en "Cosas Portfolio/task-manager-windows"
./build.ps1 -Target android   # .apk y .aab en artifacts/android
```

### Windows (.exe)

`build.ps1 -Target windows` publica una app **autocontenida y sin empaquetar** en `../task-manager-windows`:
no requiere instalar .NET ni el Windows App SDK. Para ejecutarla, abrí `task-manager-windows/GestorTareas.exe`
(se puede copiar la carpeta entera a otra PC con Windows 10 1809+ x64).

Equivale a:

```powershell
dotnet publish src/TaskManager.App -f net10.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64 `
  -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true `
  -o ../task-manager-windows
```

Los datos quedan en `%LOCALAPPDATA%\GestorTareas`.

### Android (APK / AAB)

`build.ps1 -Target android` genera en `artifacts/android`:

- `com.portfolio.gestortareas-Signed.apk`: para instalar directamente en un dispositivo.
- `com.portfolio.gestortareas-Signed.aab`: para publicar en Google Play.

Para instalarlo, activá *Opciones de desarrollador → Depuración USB* y conectá el teléfono:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" install -r artifacts/android/com.portfolio.gestortareas-Signed.apk
```

O copiá el APK al teléfono y abrilo (permitiendo “instalar apps de fuentes desconocidas”).
Compatibilidad: Android 7.0 (API 24) o superior.

**Firma de publicación:** sin configuración, el paquete se firma con la clave de depuración (sirve para probar).
Para publicar, creá un keystore y definí las variables antes de compilar:

```powershell
keytool -genkeypair -v -keystore gestortareas.keystore -alias gestortareas -keyalg RSA -keysize 2048 -validity 10000
$env:ANDROID_KEYSTORE = "C:\ruta\gestortareas.keystore"
$env:ANDROID_KEYSTORE_ALIAS = "gestortareas"
$env:ANDROID_KEYSTORE_PASSWORD = "<contraseña>"
./build.ps1 -Target android
```

### Depurar

```powershell
dotnet build src/TaskManager.App -f net10.0-windows10.0.19041.0 -t:Run   # Windows
dotnet build src/TaskManager.App -f net10.0-android -t:Run                 # Android (emulador o dispositivo conectado)
```

## Estado de verificación

- **Windows:** probado de punta a punta sobre el `.exe` (registro, sesión persistente, tablero, arrastrar y soltar entre
  columnas y dentro de una columna, editor, comentarios, alertas, diseño angosto) y verificado que el archivo de la base
  está cifrado.
- **Android:** el APK/AAB compila y se firma, pero todavía no se probó en un dispositivo ni en un emulador.

## Tests

```powershell
dotnet test tests/TaskManager.Core.Tests
```

Cubren: hash de contraseñas, saneamiento de entradas, cifrado de la base (el archivo no contiene texto plano y no abre
con otra clave), registro/login/bloqueo/expiración y revocación de sesiones, aislamiento entre usuarios, arrastrar y
soltar (orden y cambio de columna), filtros combinados, alertas de vencimiento, comentarios e historial, y borrados en cascada.
