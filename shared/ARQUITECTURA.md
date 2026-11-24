Vamos con **`ARQUITECTURA.md`**. Es corto (1 página). Sin bloque exterior de markdown para que no se corte al copiar.

---

# Arquitectura — PokeLocke

> **Versión**: 1.0
> **Última actualización**: 2026-10-21

Documento de contexto rápido. Sirve para que cualquier IA (Gemini, DeepSeek, etc.) entienda en 30 segundos cómo encaja el sistema antes de tocar código.

---

## 1. Diagrama

```
┌────────────────────┐         ┌──────────────────────┐
│   App Android      │◄───────►│                      │
│   (Kotlin)         │         │   Firebase           │
│   - UI             │         │   - Auth             │
│   - Lógica juego   │         │   - Realtime DB      │
│   - Escribe cards  │         │   (pokelocke-orion)  │
└────────────────────┘         │                      │
                               │                      │
┌────────────────────┐         │                      │
│  LockeLauncher     │◄───────►│                      │
│  (Windows)         │         │                      │
│  - Lee launcherQ.  │         └──────────────────────┘
│  - Aplica al juego │
│    (memoria/RAM)   │
└────────────────────┘
```

---

## 2. Componentes

| Componente | Tecnología | Carpeta | Responsable |
|------------|-----------|---------|-------------|
| App Android | Kotlin, Android Studio | `android-app/` | Gemini |
| LockeLauncher | Windows (DeepSeek Harness) | `windows-app/` | DeepSeek |
| Backend | Firebase RTDB + Auth (plan Spark) | Consola Firebase | Yo |

---

## 3. Flujo de comunicación

1. Usuario se autentica en la app Android → Firebase Auth (Email/Password o Anonymous).
2. La app Android escribe el estado del locke en `/lockes/{id}` y en `/user_lockes/{uid}/{id}`.
3. Cuando el usuario usa una carta o gacha, Android **encola** una acción en `/lockes/{id}/players/{tag}/launcherQueue/{pushId}` con `status: "pending"`.
4. El **LockeLauncher (Windows)** escucha esa cola, reclama la acción (`applying`), la aplica al juego (por memoria/RAM), y la marca como `applied` o `failed`.
5. Android lee el `status` para mostrar feedback al usuario.

**Importante:** el `launcherQueue` es el **único canal real** de "Android escribe → Windows aplica". Todo lo demás (monedas, inventario, stats) lo gestiona Android directamente; el launcher solo ejecuta efectos sobre la partida.

---

## 4. Entornos

| Entorno | Proyecto Firebase | Uso |
|---------|-------------------|-----|
| dev / prod | `pokelocke-orion` (único) | No hay separación dev/prod. Se usa el mismo proyecto. |

---

## 5. Restricciones técnicas

- **Plan Spark** (gratuito): no hay Cloud Functions ni Admin SDK server-side propio. Toda la lógica vive en los clientes.
- **RTDB**, no Firestore. El esquema es jerárquico, ver `CONTRATO_RTDB.md`.
- El LockeLauncher necesita **Admin SDK** o reglas especiales para poder actualizar `launcherQueue` después de crearlo (las reglas actuales son create-only, ver §8 del contrato).
- Android **no** puede acceder al proceso del juego en Windows. Toda la interacción pasa por RTDB.
- Windows **no** escribe en `/lockes` (excepto `launcherQueue` y `players/{tag}/notifications` vía el flujo de la cola).

---

## 6. Estado de las reglas de seguridad

Se mantienen **permisivas a propósito** (proyecto entre amigos). Ver §8 de `CONTRATO_RTDB.md`.

Si en algún momento se endurecen, se hará en un documento aparte (`CONTRATO_RTDB_v2.md`) y se actualizará esta sección.

---

## 7. Enlaces útiles

- Consola Firebase: https://console.firebase.google.com/project/pokelocke-orion
- Documentación RTDB: https://firebase.google.com/docs/database
- Sprites Pokémon (usado en `myTeam.slots[].spriteUrl`): https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/official-artwork/{id}.png