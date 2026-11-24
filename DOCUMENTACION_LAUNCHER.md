# PokeLocke Launcher — Documentación de Pantallas y Funcionalidades

> **Nota:** Este documento describe la interfaz de usuario del launcher PokeLocke tras las últimas modificaciones (estilo Pokelobby, icono de aplicación, pantalla de Ajustes embebida y nuevas pantallas de Inicio y Locke en vivo).

---

## 1. Resumen general

El **PokeLocke Launcher** es una aplicación WPF (.NET 10) que sirve de centro de gestión para partidas randomizadas de Pokémon. Permite:

- Gestionar una biblioteca de ROMs con carátulas.
- Lanzar la última partida jugada ("Continuar Partida").
- Conectarse a Locke en vivo vía Firebase para sincronizar equipos con la app móvil.
- Randomizar ROMs usando categorías al estilo Pokelobby.
- Configurar tema, fondo, sonidos, emulador de Switch y conexión Firebase desde la propia ventana.

Los datos del usuario se almacenan en `%APPDATA%\PokeLockeLauncher` (sobrevive a desinstalaciones gracias a Roaming).

---

## 2. Pantallas del launcher

El launcher tiene **5 pantallas principales**, accesibles desde la barra lateral izquierda:

| Pantalla | Ícono | Descripción |
|----------|-------|-------------|
| **Inicio** | 🏠 | Pantalla principal con el botón "Continuar Partida". |
| **Biblioteca** | 📚 | Gestión de ROMs, carátulas y acciones por ROM. |
| **Locke en vivo** | 🔒 | Conexión Firebase, selección de locke/jugador, publicación de equipos y registro. |
| **Randomizador** | 🎲 | Randomización de ROMs por categorías (estilo Pokelobby). |
| **Ajustes** | ⚙ | Configuración de tema, fondo, sonidos, emulador, carpetas y Firebase. |

---

## 3. Pantalla: Inicio

Esta es la pantalla de arranque del launcher. Tras los cambios recientes:

- **Única acción disponible:** un botón grande `▶  Continuar Partida` que lanza la última ROM jugada de la biblioteca (o la primera con partida guardada).
- Justo debajo, un `TextBlock` muestra información de la última ROM detectada (nombre y fecha de última partida) o un aviso si aún no hay ROMs en la biblioteca.
- Ya no contiene el antiguo apartado "Acciones Rápidas" con botones de Biblioteca y Randomizador; esos se acceden desde la barra lateral.

### Flujo al pulsar "Continuar Partida"

1. Se busca la ROM más recientemente jugada (`RomDeLaUltimaPartida`).
2. Si no hay ninguna, se muestra un aviso pidiendo añadir una ROM desde la Biblioteca.
3. Si se encuentra, se invoca `JugarRomAsync(rom)` para iniciar la partida.

---

## 4. Pantalla: Biblioteca de ROMs

Pantalla integrada en la ventana principal (no ventana aparte). Permite gestionar la colección de ROMs del usuario.

### Elementos principales

| Elemento | Función |
|----------|---------|
| **Lista de ROMs** | Muestra cada ROM con carátula, nombre, consola, juego detectado y última partida. |
| **➕ Añadir ROM** | Abre un diálogo para seleccionar un archivo ROM o ejecutable (.exe para fangames como Pokémon Añil). |
| **🗑 Borrar** | Elimina la ROM seleccionada de la biblioteca. |
| **🔄 Recargar** | Vuelve a cargar la biblioteca desde disco y migra .exe no reconocidos como Pokémon Añil. |
| **Ruta de datos** | Muestra la carpeta donde se guardan library.json, huellas.json, etc. |

### Detalle de la ROM seleccionada

Al elegir una ROM de la lista, se muestra un panel derecho con:

- **Carátula** (imagen de 120×120, editable con "Cambiar carátula").
- **Nombre** de la ROM.
- **Información** (consola, juego detectado).
- **Ruta del guardado** y **última partida**.
- Botones de acción:
  - **▶ JUGAR** — lanza la ROM directamente.
  - **🐚 Añadir Escamas Corazón** — aplica modificaciones de objetos al .sav.
  - **🍬 Añadir Carameloraros** — aplica caramelos raros al equipo.
  - **🔗 Vincular a locke** — vincula la partida con un locke de Firebase.
  - **🖼 Cambiar carátula** — abre el diálogo `CaratulaDialogos` para elegir o quitar la imagen de portada.

---

## 5. Pantalla: Locke en vivo

Pantalla dedicada (separada de Inicio) para la sincronización con la aplicación móvil **Pokelocke Android** a través de Firebase Realtime Database.

### Elementos principales

| Elemento | Función |
|----------|---------|
| **Locke** | ComboBox para seleccionar el locke al que conectarse. |
| **Jugador** | ComboBox para elegir el jugador dentro del locke. |
| **Conectar en vivo** | Inicia la conexión con Firebase para sincronizar el estado. |
| **📤 Publicar equipo del .sav → app** | Sube el equipo actual del archivo de guardado (.sav) a la aplicación móvil. |
| **Registro** | Área de log con timestamp que muestra eventos del sistema y de la conexión. Incluye botón "Limpiar". |

### Nota sobre el registro

El `TextBlock` de registro se comparte visualmente entre la pantalla de Inicio (oculta) y Locke en vivo; los mensajes de log se escriben en ambos controles para mantener un historial unificado.

---

## 6. Pantalla: Randomizador

Pantalla de randomización de ROMs organizada por **pestañas** al estilo Pokelobby. Cada pestaña es una categoría de opciones del randomizador.

### Pestañas (categorías)

| Pestaña | Subsecciones / Tarjetas |
|---------|------------------------|
| **Características** | Tipos, Habilidades, Estadísticas Base, Evoluciones |
| **Iniciales y Estáticos** | Iniciales, Estáticos, Intercambios |
| **Movimientos** | Movesets, Datos de Movimiento, MT/MO, Tutores |
| **Pokémon Salvajes** | Salvajes, Tótems |
| **Entrenadores** | Modo de Entrenadores, Opciones Generales, Nivel de Entrenadores, Legendarios, Mega Evolución, Ajuste de Fuerza |
| **Objetos** | Objetos de Campo, Tiendas, Recogida |
| **Miscelánea** | Varios, Liga |

### Diseño de cada tarjeta

- **Una tarjeta por categoría base** (Modo + Opciones juntos, no separadas).
- **Grid uniforme** con columnas configurables:
  - Entrenadores: 4 columnas.
  - Miscelánea y Liga: 3 columnas.
  - Resto: 2 columnas.
- **Controles al estilo Pokelobby:**
  - Radio buttons para claves `.mod` (modo: Sin cambios / Aleatorio / Completamente aleatorio).
  - Checkboxes con el texto de etiqueta justo al lado del control.
  - Sliders con valor numérico visible.
  - ComboBoxes para selección de opciones.

---

## 7. Pantalla: Ajustes (embebida)

A diferencia de versiones anteriores, **Ajustes ya no abre una ventana adicional (`ShowDialog`)**. Ahora es un panel (`SettingsPanel`, UserControl) embebido directamente en la ventana principal, accesible desde el botón `⚙ Ajustes` de la barra lateral.

### Secciones del panel de Ajustes

| Sección | Opciones |
|---------|----------|
| **Tema de acento** | 16 presets de color (selector por botones circulares). Se aplica al instante. |
| **Fondo** | 8 presets de color + opción de imagen o vídeo de fondo. Botones "Elegir imagen/vídeo…" y "Quitar fondo". |
| **Vídeo con sonido** | Checkbox + slider de volumen (solo visible si hay fondo de vídeo). |
| **Sonidos de la interfaz** | Checkbox para activar/desactivar sonidos al navegar. |
| **Emulador de Switch** | Ruta del ejecutable (.exe) y argumentos (con placeholder `{rom}`). Botón "Quitar". |
| **Carpetas y conexión** | Carpeta vendor, carpeta de datos, URL de Firebase RTDB, Web API Key de Firebase. |

### Persistencia

Al cerrar la pantalla o cambiar valores, se invoca `AplicarCambios()` que:
1. Guarda todos los valores en `ajustes.json` (vía `Ajustes.Guardar()`).
2. Notifica el evento `CambiosAplicados` para reaplicar tema, fondo, sonidos y vídeo sin reiniciar.

---

## 8. Icono de la aplicación

- **Archivo:** `LockeLauncher/pokelocke.png`
- **Integración:** Se incluye como `Resource` en el proyecto (`.csproj`) y se carga en el constructor de `MainWindow` mediante `pack://application:,,,/pokelocke.png`.
- Se muestra en la barra de título de la ventana y en el icono del ejecutable.

---

## 9. Almacenamiento y rutas

| Ruta | Contenido |
|------|-----------|
| `%APPDATA%\PokeLockeLauncher\ajustes.json` | Configuración del usuario (tema, fondo, rutas, Firebase, etc.). |
| `%APPDATA%\PokeLockeLauncher\datos\` | Biblioteca, huellas, carátulas y vínculos de locke. |
| `%LOCALAPPDATA%\PokeLockeLauncher\` | Instalación de Velopack (se borra al desinstalar). |

**Migración:** Al iniciar, el launcher copia (sin sobresbir) los archivos `library.json`, `huellas.json`, `covers.json` y `vinculos.json` desde carpetas anteriores de PokeLobby si aún no existen datos propios.

---

## 10. Stack tecnológico

| Componente | Tecnología |
|------------|------------|
| Framework | WPF (.NET 10, `net10.0-windows`) |
| Lenguaje | C# |
| UI | XAML con `DynamicResource` para theming |
| Actualizaciones | Velopack 1.2.161 |
| Manipulación de saves | PKHeX.Core 26.5.5 |
| Tiempo real | Microsoft.AspNetCore.SignalR.Client 10.0.0 (Firebase RTDB) |
| Pruebas | xUnit 3 (265 tests) |

---

## 11. Flujo típico de uso

1. **Primera vez:** El launcher migra datos antiguos de PokeLobby si existen, crea la carpeta de datos en `%APPDATA%` y muestra la pantalla de Inicio.
2. **Añadir ROMs:** Desde Biblioteca → "➕ Añadir ROM", se registran los archivos de juego.
3. **Jugar:** Desde Inicio → "▶ Continuar Partida" lanza la última partida, o desde Biblioteca → "▶ JUGAR" una ROM específica.
4. **Randomizar:** Desde Randomizador, se eligen las categorías y opciones deseadas, luego se aplica la randomización a la ROM seleccionada.
5. **Conectar Locke:** Desde Locke en vivo, se vincula la partida con la app móvil para sincronizar equipos.
6. **Configurar:** Desde Ajustes, se personaliza tema, fondo, emulador y conexión Firebase sin salir de la ventana principal.

---

*Documentación generada a partir del código fuente del proyecto `LockeLauncher/src/PokeLobby.LauncherUi`.*
