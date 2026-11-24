# Contrato Realtime Database — PokeLocke

> **Versión**: 1.0
> **Última actualización**: 2026-10-21
> **Proyecto Firebase**: `pokelocke-orion`
> **Instancia RTDB**: `pokelocke-orion-default-rtdb` (europe-west1)
> **Plan**: Spark

Este documento es la fuente de verdad del esquema de datos. Ambas apps (Android y Windows) deben leerlo antes de implementar cualquier cambio. Si algo no cuadra con el export real, gana el export.

---

## 1. Autenticación

| Método | Estado |
|--------|--------|
| Email/Password | habilitado |
| Anonymous | habilitado |

El UID de Firebase Auth es la clave primaria del usuario (`users/{uid}`).

**Formato del tag de usuario**:

- Formato `#`: `usuario#discriminator` → ej. `"freme#4933"`
- Formato `_`: `usuario_discriminator` → ej. `"freme_4933"`

`discriminator` es un string de 4 dígitos (`"4933"`, `"0000"`, etc.).

---

## 2. Árbol de nodos raíz

| Nodo | Propósito | Subnodos |
|------|-----------|----------|
| `/users` | Perfil de usuario | Sí |
| `/usernames` | Índice `usernameLower → uid` | No (mapa plano) |
| `/global_tags` | Índice `tagWord → code4d → uid` | Sí |
| `/lockes` | Partidas principales | Sí (profundo) |
| `/user_lockes` | Índice `uid → lockeId → resumen` | Sí |
| `/profiles` | Estado de Dualockes (2 jugadores) | Sí |
| `/user_profiles` | Índice `uid → profileId → resumen` | Sí |

---

## 3. Esquema por nodo

### 3.1 `/users/{uid}`

| Campo | Tipo | Obligatorio | Ejemplo | Notas |
|-------|------|-------------|---------|-------|
| `usuario` | string | sí | `"Kakkaroto"` | Case-sensitive |
| `discriminator` | string (4 dígitos) | sí | `"9062"` | |
| `pass` | string | no | `"123456"` | DEPRECATED. No usar. Eliminar en migración |
| `friends` | array\<string\> | no | `["freme#4933","jopetas#8829"]` | Mezcla `#` y `_` |

### 3.2 `/usernames/{usernameLower}`

Mapa plano: clave = username en minúsculas, valor = uid (string).

```json
{ "kakkaroto": "UmluTJcZeUQkYfxnsNPD8poqcFG3" }
```

### 3.3 `/global_tags/{tagWord}/{code4d}`

Mapa `palabra → código de 4 dígitos → uid`. Duplica `users.usuario + discriminator`. DEPRECATED a favor de buscar en `/users`.

### 3.4 `/lockes/{lockeId}`

`lockeId` es un UUID v4 generado en cliente. Existen 3 variantes de estructura que ambas apps deben soportar:

**Variante A — "legacy dualocke" (sin `players`)**

Campos en el documento raíz: `codigo`, `creatorUser`, `id`, `nombre`, `megasEnabled`, `pkhexEnabled`, `soloRun`, `inventario`, `monedas`, `monedas2`, `prioridadOro`, `prioridadOro2`, `stats`, `stats2`, `ticketsPremium`, `ticketsPremium2`, `history`.

`juego` puede faltar.

**Variante B — "single/multi player con `players`"**

Campos: `codigo`, `creatorUser`, `id`, `nombre`, `juego`, `lastActive`, `megasEnabled` y/o `isMegasEnabled`, `pkhexEnabled` y/o `isPkhexEnabled`, `soloRun` y/o `isSoloRun`, `shinylocke` y/o `isShinylocke`, `players`, `history`.

`players` puede tener 1 o 2 entradas.

**Variante C — "camelCase puro" (la que escriben las apps nuevas)**

Solo usa `isMegasEnabled`, `isPkhexEnabled`, `isShinylocke`, `isSoloRun`. `players` puede tener 1 o 2 entradas.

**Tabla unificada de campos de `/lockes/{lockeId}`**

| Campo | Tipo | Ejemplo | Notas |
|-------|------|---------|-------|
| `id` | string (UUID) | `"7e4b6462-..."` | Igual que la clave |
| `codigo` | string (4 dígitos) | `"2712"` / `"0000"` | |
| `nombre` | string | `"Debug"`, `"Hardlocke"` | |
| `juego` | string | `"Blanco"`, `"Rojo Fuego"` | Puede faltar |
| `creatorUser` | string (`#`) | `"freme#4933"` | A veces `""` |
| `lastActive` | number (epoch ms) | `1792581445889` | |
| `megasEnabled` / `isMegasEnabled` | boolean | `true` | Ambos válidos |
| `pkhexEnabled` / `isPkhexEnabled` | boolean | `true` | Ambos válidos |
| `soloRun` / `isSoloRun` | boolean | `false` | Ambos válidos |
| `shinylocke` / `isShinylocke` | boolean | `true` | Ambos válidos |
| `players` | object\<tag, Player\> | ver 3.5 | |
| `history` | array\<Event\> | ver 3.9 | |
| `inventario` | array\<Item\> | ver 3.7 | Solo variante A |
| `monedas`, `monedas2` | number | `10` | Solo variante A |
| `stats`, `stats2` | object | ver 3.8 | Solo variante A |

### 3.5 `/lockes/{lockeId}/players/{playerTag}`

`playerTag` es `usuario_discriminator` (formato `_`).

| Campo | Tipo | Ejemplo | Notas |
|-------|------|---------|-------|
| `playerTag` | string (`_`) | `"yo_6109"` | = clave |
| `monedas` | number | `1047` | |
| `prioridadOro` | number | `40` | |
| `ticketsPremium` | number | `1` | |
| `ticketsEvolucion` | number | `0` | Puede faltar |
| `ticketsMentas` | number | `0` | Puede faltar |
| `ticketsObjetos` | number | `0` | Puede faltar |
| `inventario` | array\<Item\> | ver 3.7 | |
| `stats` | object | ver 3.8 | |
| `lastCardId` | number | `-1`, `78` | |
| `doubleRouletteActive` | boolean | `false` | |
| `compoundInterest` | object | `{active, lockedGold, losses, wins}` | |
| `gymPacksClaimed` | array\<number\> | `[0,1,2,3,4,5,6,7]` | |
| `myTeam` / `rivalTeam` | object | `{slots: [...]}` | ver 3.6 |
| `notifications` | array\<Notif\> | `[{id, message, timestamp, type}]` | |
| `launcherQueue` | object\<pushId, QueueItem\> | ver 3.10 | Lo consume el LockeLauncher (Windows) |

### 3.6 `myTeam` / `rivalTeam` (dentro de `players/{tag}`)

```json
{
  "slots": [
    {
      "id": 715,
      "name": "Noivern",
      "spriteUrl": "https://...",
      "ability": "Impulso",
      "types": ["Flying", "Dragon"],
      "baseStats": { "hp": 85, "atk": 70, "def": 80, "spa": 97, "spd": 80, "spe": 123 },
      "ivs":       { "hp": 31, "atk": 31, "def": 31, "spa": 31, "spd": 31, "spe": 31 },
      "evs":       { "hp": 0,  "atk": 0,  "def": 0,  "spa": 0,  "spd": 0,  "spe": 0  },
      "stages":    { "atk": 0, "def": 0, "spa": 0, "spd": 0, "spe": 0 },
      "level": 50,
      "nature": "MODEST",
      "isShiny": true,
      "moves": ["", "", "", ""]
    }
  ]
}
```

### 3.7 `Item` (dentro de `inventario`)

| Campo | Tipo | Ejemplo | Notas |
|-------|------|---------|-------|
| `id` | number | `14` | Colisiona entre tipos (id 1 = CARTA, SOBRE u OBJETO según el `type`) |
| `quantity` | number | `5` | |
| `type` | string | `"CARTA"` \| `"SOBRE"` \| `"OBJETO"` | |

Identidad real de un item: (`type`, `id`). Las apps deben buscar por ambos.

### 3.8 `stats` / `stats2`

Objeto libre con contadores. Claves posibles (todas opcionales):

`cartasUsadas`, `cartasUsadasPorRareza`, `cartasConseguidasPorRareza`, `jackpotsTragaperras`, `muertesTragaperras`, `oroGanadoRuleta`, `oroGastado`, `sobresAbiertos`, `victoriasDuelo`, `tiradasSlot`, `tiradasRuleta`, `resultadosRuleta`, `resultadosSlot`, `desgloseSobres`, `desgloseOro`, `desgloseOroRuleta`, `desgloseOroVentas`, `oroGanado`, `usoCartas`, `ventasEfectuadas`, `playerTag`.

Los sub-objetos (`desgloseSobres`, `resultadosRuleta`, `usoCartas`, etc.) son objects con claves string compuestas, ejemplos:

- `desgloseSobres`: `"Detail_Sobre épico_Opened_Bought": 2`
- `resultadosRuleta`: `"normal_+1 Oro": 3`, `"combat_Ticket Objeto": 1`
- `usoCartas`: `"26": 1` o `"carta_26": 1` (ambos formatos existen)

`stats.playerTag` suele estar vacío `""`.

### 3.9 `Event` (dentro de `history`)

| Campo | Tipo | Ejemplo |
|-------|------|---------|
| `description` | string | `"Abrió Sobre épico"` |
| `player` | string (`#` o `_`) | `"freme#4933"` |
| `timestamp` | number (epoch ms) | `1783556289198` |

`history` puede ser array u object (ver sección 6).

### 3.10 `launcherQueue/{pushId}` (dentro de `players/{tag}`)

Cola de acciones que el LockeLauncher (Windows) consume para aplicar efectos al juego. Es el único canal real de "Android escribe → Windows aplica".

| Campo | Tipo | Obligatorio | Ejemplo | Notas |
|-------|------|-------------|---------|-------|
| `schema` | number | sí | `1` | Siempre `1` |
| `type` | string | sí | `"CARD_EFFECT"` \| `"GACHA_POKEMON"` \| `"ITEM_GRANT"` \| `"MINT_USE"` | |
| `status` | string | sí | `"pending"` \| `"applying"` \| `"applied"` \| `"failed"` \| `"skipped"` | |
| `createdAt` | number | sí | `1792581007499` | |
| `createdBy` | string (`_`) | sí | `"Kakkaroto_9062"` | |
| `appVersion` | string | sí | `"1.0"` | |
| `claimedAt` / `claimedBy` | number / string | no | | Cuando el launcher lo recoge |
| `appliedAt` / `appliedBy` | number / string | no | `"LockeLauncher 1.1"` | |
| `resultMessage` / `errorMessage` | string | no | | |
| `cardId` / `cardName` | number / string | solo CARD_EFFECT | `74` / `"Medicina experimental"` | |
| `speciesId` / `speciesName` / `ball` / `gameGen` / `isShiny` | number / string / string / number / boolean | solo GACHA_POKEMON | | |
| `params` | object | no | `{ success: true }` | Uso libre |

Flujo: Android escribe `pending` → LockeLauncher lo reclama (`applying`) → aplica en el juego → marca `applied` o `failed`. Android/Windows leen el estado para mostrar feedback.

### 3.11 `/profiles/{profileId}` — Dualocke

Estado espejo para partidas de 2 jugadores en la misma consola. Tiene campos paralelos (`X` y `X2`) y un `secondPlayer`.

| Campo | Tipo | Ejemplo |
|-------|------|---------|
| `id` | string | `"08be6098-..."` |
| `codigo` | string | `"2083"` |
| `nombre` | string | `"50"` |
| `creatorUser` | string (`#`) | `"si#8548"` |
| `secondPlayer` | string (`#`) | `"tu#3361"` |
| `megasEnabled` / `pkhexEnabled` / `soloRun` | boolean | |
| `monedas` / `monedas2` | number | `50` / `0` |
| `prioridadOro` / `prioridadOro2` | number | |
| `ticketsPremium` / `ticketsPremium2` | number | |
| `inventario` / `inventario2` | array\<Item\> | |
| `stats` / `stats2` | object | |
| `history` | array u object | ver sección 6 |
| `notifications2` | object | `{ "0": {...}, "-Ow...": {...} }` |

### 3.12 `/user_lockes/{uid}/{lockeId}`

Resumen del locke en el índice del usuario. Permite listar sin descargar `/lockes` entero.

| Campo | Tipo | Ejemplo | Notas |
|-------|------|---------|-------|
| `id` | string | `"7e4b6462-..."` | = clave |
| `codigo` | string | `"0000"` | |
| `nombre` | string | `"debug"` | |
| `juego` | string | `"Rojo Fuego"` | |
| `megasEnabled` / `isMegasEnabled` | boolean | | Ambos válidos |
| `pkhexEnabled` / `isPkhexEnabled` | boolean | | Ambos válidos |
| `soloRun` / `isSoloRun` | boolean | | Ambos válidos |
| `shared` / `isShared` | boolean | | Ambos válidos, puede faltar |
| `shinylocke` / `isShinylocke` | boolean | | Ambos válidos |
| `lastActive` | number | | Puede faltar |

Aviso: hay al menos una entrada donde el valor es `true` en lugar de un objeto (`user_lockes/fzWtose8.../f37e74fc... = true`). Las apps deben tolerarlo y tratarlo como "sin datos".

### 3.13 `/user_profiles/{uid}/{profileId}`

Igual que `user_lockes` pero para Dualockes. Mismos campos y mismas inconsistencias.

---

## 4. Relaciones entre nodos

| Origen | Destino | Cómo |
|--------|---------|------|
| `lockes/{id}` | `user_lockes/{uid}/{id}` | El locke se duplica como resumen al crearlo (para el creador y para el 2º jugador) |
| `profiles/{id}` | `user_profiles/{uid}/{id}` | Igual |
| `lockes/{id}.creatorUser` | `users/{uid}` | Vía `usuario#disc` → buscar en `/users` |
| `lockes/{id}.players[tag]` | `users/{uid}` | Vía `usuario_disc` → buscar en `/users` |
| `lockes/{id}.history[].player` | `users/{uid}` | Vía tag (formato variable) |
| `users/{uid}.friends[]` | `users/{uid}` | Vía tag (formato variable) |
| `usernames/{usernameLower}` | `users/{uid}` | Directo |
| `global_tags/{word}/{code}` | `users/{uid}` | Directo (DEPRECATED) |

---

## 5. Convenciones

- Timestamps: epoch en milisegundos (number). Ej. `1783556289198`.
- IDs de locke/profile: UUID v4 generados en cliente.
- IDs de `launcherQueue`: claves push de RTDB.
- Nombres de campo: camelCase.
- Tags de usuario: coexisten `usuario#disc` y `usuario_disc`. Nunca normalizar a ciegas: leer el formato que venga del campo.
- Booleanos de feature: coexisten `flag` e `isFlag`. Ambas apps deben leer cualquiera de los dos, priorizando `isFlag` si ambos existen.
- Colecciones tipo array: `history` puede ser array u object; tratar siempre como colección iterable.
- Borrado: eliminar el nodo (no hay soft delete generalizado).

---

## 6. Inconsistencias conocidas (documentadas, no bloquean)

Estas cosas están así en producción. No son bugs a arreglar ahora; las apps deben tolerarlas.

| # | Inconsistencia | Impacto | Mitigación en cliente |
|---|----------------|---------|-----------------------|
| 1 | Tag `#` vs `_` mezclados | Medio | Normalizar al comparar; guardar el formato original |
| 2 | `flag` vs `isFlag` en booleanos | Bajo | Leer ambos, preferir `isFlag` |
| 3 | `history` array vs object | Medio | Iterar como colección |
| 4 | `notifications` array vs object (`notifications2`) | Bajo | Idem |
| 5 | `lockes` con 3 variantes de estructura | Alto | Detectar `players` vs campos raíz |
| 6 | `user_lockes/{uid}/{id} = true` (booleano) | Alto | Tratar como objeto vacío |
| 7 | `creatorUser: ""` en algunos lockes | Medio | Tratar vacío como "desconocido" |
| 8 | `juego` ausente en variante A | Bajo | Fallback a `""` |
| 9 | `pass` en texto plano (DEPRECATED) | Alto (seguridad) | No usar ni replicar; eliminar en migración |
| 10 | `usernames` incompleto (faltan usuarios) | Bajo | Fallback a buscar en `/users` |
| 11 | `discriminator` duplicado (`"0000"`) | Medio | Usar uid como identidad real |
| 12 | `usoCartas` con claves `"26"` y `"carta_26"` | Bajo | Aceptar ambos formatos al leer |
| 13 | `stats.playerTag` vacío `""` | Bajo | Ignorar; usar el tag de la clave |
| 14 | `profiles.history` a veces object | Medio | Iterar como colección |

---

## 7. Índices recomendados (`.indexOn`)

Añadir a `database.rules.json` cuando se toquen las reglas:

```json
"lockes":                 { ".indexOn": ["creatorUser", "lastActive", "juego", "codigo"] },
"lockes.$lockeId.history":{ ".indexOn": ["timestamp", "player"] },
"lockes.$lockeId.players.$playerTag.launcherQueue": { ".indexOn": ["status", "createdAt"] },
"users":                  { ".indexOn": ["usuario", "discriminator"] },
"user_lockes.$uid":       { ".indexOn": ["lastActive", "juego"] }
```

Consultas que habilitan:

| Query | Índice |
|-------|--------|
| `lockes.orderByChild('creatorUser').equalTo(tag)` | `lockes.creatorUser` |
| `lockes.orderByChild('lastActive').limitToLast(N)` | `lockes.lastActive` |
| `lockes/{id}/history.orderByChild('timestamp')` | `lockes.$lockeId.history.timestamp` |
| `launcherQueue.orderByChild('status').equalTo('pending')` | `...launcherQueue.status` |
| `user_lockes/$uid.orderByChild('lastActive')` | `user_lockes.$uid.lastActive` |
| `users.orderByChild('usuario').equalTo(x)` | `users.usuario` |

---

## 8. Estado de seguridad

Las reglas actuales no se tocan en esta versión del contrato (proyecto entre amigos). Se documentan como están en `shared/database.rules.json`.

Puntos conocidos a tener en cuenta al implementar:

- `users/{uid}/friends`, `friendRequests`, `invites` son escribibles por cualquier autenticado.
- `lockes/{id}` se puede crear con cualquier `creatorUser` y lo puede borrar cualquier jugador.
- `launcherQueue` es create-only; el ciclo de estados lo hace el launcher vía Admin SDK.
- `/lockes` es legible por cualquier autenticado.