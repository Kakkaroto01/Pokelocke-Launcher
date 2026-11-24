using System;
using System.Collections.Generic;

namespace PokeLobby.LauncherUi;

/// <summary>
/// Descripciones en español de las opciones del Randomizer (para el tooltip que se ve al pasar el
/// ratón). Cada clave del motor tiene su explicación y, si no existe, se usa la de su categoría.
/// </summary>
internal static class DescripcionesRandomizer
{
    private static readonly Dictionary<string, string> Exactas = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── habilidades ──
        ["abilities.mod"] = "Qué hacer con las habilidades de los Pokémon: sin cambios, aleatorias, cambiadas por las de otra especie, etc.",
        ["abilities.allowWonderGuard"] = "Permite que salga Superguarda al aleatorizar habilidades (puede hacer Pokémon casi invulnerables).",
        ["abilities.banBad"] = "Descarta habilidades malas o inútiles al azar (Truante, Ausente, etc.) para que no toquen.",
        ["abilities.banNegative"] = "Descarta habilidades perjudiciales (Bucle, Flaqueza, etc.).",
        ["abilities.banTrapping"] = "Descarta habilidades que atrapan al rival (Sombra Trampa, Arena Trampa, etc.).",
        ["abilities.combineDuplicate"] = "Une habilidades repetidas en el equipo para que no haya dos Pokémon con la misma.",
        ["abilities.ensureTwo"] = "Cada Pokémon tendrá al menos dos habilidades posibles (normal + oculta).",
        ["abilities.followEvolutions"] = "La habilidad sigue la misma línea evolutiva (las preevoluciones mantienen su habilidad).",
        ["abilities.followMegaEvolutions"] = "La megaevolución conserva la habilidad de su forma base.",

        // ── estadísticas base ──
        ["basestats.mod"] = "Qué hacer con las estadísticas base: sin cambios, aleatorias, intercambiadas entre especies, etc.",
        ["basestats.expCurveSpeed"] = "Curva de experiencia que se aplica si se estandarizan las curvas (rápida, lenta, errática…).",
        ["basestats.expCurveTarget"] = "A qué Pokémon se aplica la estandarización de curvas de experiencia.",
        ["basestats.followEvolutions"] = "Las preevoluciones mantienen la línea de estadísticas de su evolución.",
        ["basestats.followMegaEvolutions"] = "La megaevolución conserva las estadísticas de su forma base.",
        ["basestats.randomizeAdded"] = "Aleatoriza también las estadísticas añadidas respecto a la especie original.",
        ["basestats.standardizeExpCurves"] = "Pone la misma curva de experiencia a todos los Pokémon.",
        ["basestats.updateGeneration"] = "Actualiza las estadísticas base a las de una generación posterior.",
        ["basestats.updateGenerationTo"] = "Generación a la que se actualizan las estadísticas (0 = la del juego).",

        // ── evoluciones ──
        ["evolutions.mod"] = "Qué hacer con las evoluciones: sin cambios, aleatorias, con condiciones más fáciles, etc.",
        ["evolutions.allowAlternateForms"] = "Permite que la evolución sea una forma alternativa (Alola, Galar, Hisui…).",
        ["evolutions.changeImpossible"] = "Cambia las evoluciones imposibles de conseguir en el juego por otras que sí se puedan.",
        ["evolutions.forceChange"] = "Fuerza a cambiar las evoluciones aunque no haga falta.",
        ["evolutions.makeEasier"] = "Facilita las condiciones de evolución (niveles más bajos, sin objetos…).",
        ["evolutions.maxLevel"] = "Nivel máximo al que puede evolucionar (0 = sin límite).",
        ["evolutions.maxThreeStages"] = "Mantiene como mucho tres etapas evolutivas.",
        ["evolutions.removeTimeBased"] = "Quita las evoluciones que dependen de la hora del día.",
        ["evolutions.sameTyping"] = "La evolución conserva el tipo del Pokémon original.",
        ["evolutions.similarStrength"] = "Elige evoluciones de fuerza parecida a la original.",
        ["evolutions.tradeLevel"] = "Nivel al que evolucionan los Pokémon que evolucionaban por intercambio.",

        // ── objetos del mapa ──
        ["fielditems.mod"] = "Qué hacer con los objetos que se recogen en el mapa: sin cambios, aleatorios, etc.",
        ["fielditems.banBadItems"] = "Descarta objetos inútiles al aleatorizar los del mapa.",

        // ── varios ──
        ["misc.raceMode"] = "Modo carrera: no cambia el juego, solo lo deja preparado para competir (sin ayudas).",
        ["misc.fastestText"] = "Texto de diálogo a la velocidad máxima.",
        ["misc.fasterHPExpBars"] = "Barras de vida y experiencia mucho más rápidas.",
        ["misc.runningShoesIndoors"] = "Permite correr dentro de las casas.",
        ["misc.runWithoutRunningShoes"] = "Se puede correr sin tener las zapatillas.",
        ["misc.giveNationalDex"] = "Entrega la Pokédex Nacional desde el principio.",
        ["misc.lowerCaseNames"] = "Pone los nombres de Pokémon en minúsculas.",
        ["misc.updateTypeEffectiveness"] = "Actualiza la tabla de tipos a la de generaciones posteriores.",
        ["misc.updateRotomTypings"] = "Actualiza los tipos de las formas de Rotom.",
        ["misc.fixCritRate"] = "Corrige el cálculo de críticos de generaciones antiguas.",
        ["misc.nerfXAccuracy"] = "Baja la precisión de Precisión X.",
        ["misc.banLuckyEgg"] = "Impide que aparezca Huevo Suerte.",
        ["misc.noFreeLuckyEgg"] = "Quita el Huevo Suerte gratuito.",
        ["misc.banBigMoneyManiacItems"] = "Impide los objetos caros del Coleccionista.",
        ["misc.disableLowHPMusic"] = "Quita la música de vida baja.",
        ["misc.fastDistortionWorld"] = "Mundo Distorsión sin las plataformas lentas.",
        ["misc.forceChallengeMode"] = "Fuerza el modo desafío (solo juegos que lo tengan).",
        ["misc.allowPikachuEvolution"] = "Permite evolucionar a Pikachu.",
        ["misc.balanceStaticLevels"] = "Ajusta el nivel de los Pokémon estáticos al del equipo.",
        ["misc.dontRevertAltFormes"] = "No revierte las formas alternativas al cambiar de zona.",
        ["misc.randomizeCatchingTutorial"] = "Aleatoriza el Pokémon del tutorial de captura.",
        ["misc.randomizePCPotion"] = "Aleatoriza la poción que hay en el PC.",
        ["misc.allWildCallAllies"] = "Todos los Pokémon salvajes pueden llamar aliados (solo gen 7).",
        ["misc.bwExpPatch"] = "Aplica el parche de experiencia de Blanco/Negro.",

        // ── movimientos: datos ──
        ["movedata.updateToGeneration"] = "Actualiza los datos de los movimientos a una generación posterior.",
        ["movedata.updateGenerationTo"] = "Generación a la que se actualizan los movimientos (0 = la del juego).",
        ["movedata.randomizePower"] = "Aleatoriza la potencia de los movimientos.",
        ["movedata.randomizeAccuracy"] = "Aleatoriza la precisión de los movimientos.",
        ["movedata.randomizePP"] = "Aleatoriza los PP de los movimientos.",
        ["movedata.randomizeCategory"] = "Aleatoriza si el movimiento es físico, especial o de estado.",
        ["movedata.randomizeTypes"] = "Aleatoriza el tipo de los movimientos.",

        // ── movimientos: aprendidos al subir nivel ──
        ["movesets.mod"] = "Qué hacer con los movimientos que aprende cada Pokémon al subir de nivel.",
        ["movesets.evolutionMovesForAll"] = "Todos los Pokémon aprenden también los movimientos de evolución.",
        ["movesets.forceGoodDamaging"] = "Asegura un número mínimo de movimientos de daño útiles.",
        ["movesets.forceGoodDamagingPercent"] = "Porcentaje mínimo de movimientos de daño que se garantizan.",
        ["movesets.guaranteedLevel1Min"] = "Número mínimo de movimientos al nivel 1.",
        ["movesets.noGameBreaking"] = "Evita movimientos que rompen el juego (bloqueos, etc.).",
        ["movesets.reorderDamaging"] = "Coloca los movimientos de daño en los primeros huecos.",
        ["movesets.startWithGuaranteedMoves"] = "Empieza el juego con los movimientos garantizados ya aprendidos.",

        // ── objetos recogidos por el camino ──
        ["pickup.mod"] = "Qué objeto puede encontrar un Pokémon con Recogida.",
        ["pickup.banBadItems"] = "Descarta objetos malos en la tabla de Recogida.",

        // ── ROM ──
        ["rom.original"] = "Ruta de la ROM original (uso interno del launcher).",

        // ── tiendas ──
        ["shops.mod"] = "Qué venden las tiendas: sin cambios, aleatorio, balanceado…",
        ["shops.balancePrices"] = "Ajusta los precios al nivel de progreso de la zona.",
        ["shops.banBadItems"] = "Descarta objetos inútiles de las tiendas.",
        ["shops.banOverpoweredItems"] = "Impide vender objetos demasiado fuertes para el momento.",
        ["shops.banRegularItems"] = "Quita los objetos normales de las tiendas.",
        ["shops.guaranteeEvolutionItems"] = "Garantiza que haya objetos de evolución a la venta.",
        ["shops.guaranteeXItems"] = "Garantiza objetos de combate (X Ataque, X Defensa…).",

        // ── Pokémon iniciales ──
        ["starters.mod"] = "Iniciales: sin cambios, personalizados, aleatorios CON DOS EVOLUCIONES o totalmente aleatorios.",
        ["starters.allowAlternateForms"] = "Permite formas alternativas como inicial (Alola, Galar…).",
        ["starters.banBadItems"] = "Quita objetos inútiles a los iniciales.",
        ["starters.banLegendaries"] = "Impide que un legendario sea inicial.",
        ["starters.custom1"] = "Especie fija del primer inicial (0 = sin fijar).",
        ["starters.custom2"] = "Especie fija del segundo inicial (0 = sin fijar).",
        ["starters.custom3"] = "Especie fija del tercer inicial (0 = sin fijar).",
        ["starters.mustBeBaseForm"] = "El inicial debe ser una forma base (no una evolución).",
        ["starters.randomizeHeldItems"] = "Aleatoriza el objeto que llevan los iniciales.",

        // ── Pokémon estáticos ──
        ["statics.mod"] = "Qué hacer con los Pokémon estáticos (legendarios, regalos, fósiles…).",
        ["statics.allowAlternateForms"] = "Permite formas alternativas entre los estáticos.",
        ["statics.swapMegaEvolvables"] = "Puede convertirlos en Pokémon con megaevolución.",
        ["statics.randomize600BST"] = "Aleatoriza también los de 600 de total de estadísticas.",
        ["statics.limitMainGameLegendaries"] = "Limita los legendarios durante la historia principal.",
        ["statics.noLegendaries"] = "Convierte todos los estáticos en no legendarios.",
        ["statics.levelModifierValue"] = "PORCENTAJE aplicado al nivel de los Pokémon estáticos: 40 = +40 %.",
        ["statics.percentageLevelModifier"] = "El modificador de nivel anterior es un porcentaje, no niveles fijos.",
        ["statics.bstMargin"] = "Margen de fuerza permitido al elegir el reemplazo.",
        ["statics.fixMusic"] = "Corrige la música cuando el estático cambia de especie.",

        // ── compatibilidad de MT/MO ──
        ["tmcompat.mod"] = "Qué movimientos puede aprender cada Pokémon por MT/MO.",
        ["tmcompat.followEvolutions"] = "La compatibilidad sigue la línea evolutiva.",
        ["tmcompat.fullHMCompat"] = "Todos pueden usar todas las MO (Corte, Surf, etc.).",
        ["tmcompat.levelupMoveSanity"] = "Evita combos incoherentes con lo que aprende por nivel.",

        // ── movimientos de MT/MO ──
        ["tmmoves.mod"] = "Qué movimientos contienen las MT/MO.",
        ["tmmoves.forceGoodDamaging"] = "Garantiza movimientos de daño útiles en las MT.",
        ["tmmoves.forceGoodDamagingPercent"] = "Porcentaje mínimo de movimientos de daño.",
        ["tmmoves.keepFieldMoves"] = "Mantiene los movimientos de campo (Surf, Vuelo, etc.).",
        ["tmmoves.noGameBreaking"] = "Evita movimientos que rompen el juego.",

        // ── tótems (gen 7) ──
        ["totems.mod"] = "Qué hacer con los Pokémon tótem (solo Sol/Luna y Ultrasol/Ultraluna).",
        ["totems.allyMod"] = "Qué hacer con los aliados que llaman los tótems.",
        ["totems.auraMod"] = "Qué hacer con el aura que tienen los tótems.",
        ["totems.allowAlternateForms"] = "Permite formas alternativas en los tótems.",
        ["totems.randomizeHeldItems"] = "Aleatoriza el objeto de los tótems.",
        ["totems.levelModifierValue"] = "PORCENTAJE aplicado al nivel de los tótems: 40 = +40 %.",
        ["totems.percentageLevelModifier"] = "El modificador de nivel es porcentaje, no niveles fijos.",

        // ── intercambios dentro del juego ──
        ["trades.mod"] = "Qué hacer con los intercambios internos del juego (el Pokémon pedido y el entregado).",
        ["trades.randomizeNicknames"] = "Aleatoriza el mote de los Pokémon intercambiados.",
        ["trades.randomizeIVs"] = "Aleatoriza los IVs de los Pokémon intercambiados.",
        ["trades.randomizeItems"] = "Aleatoriza el objeto que llevan.",
        ["trades.randomizeOTs"] = "Aleatoriza el entrenador original de los intercambiados.",

        // ── entrenadores ──
        ["trainers.mod"] = "Qué hacer con los equipos de los entrenadores: sin cambios, aleatorios, distribuidos por fuerza…",
        ["trainers.similarStrength"] = "Los equipos se cambian por otros de fuerza parecida.",
        ["trainers.percentageLevelModifier"] = "Casilla heredada del motor; en entrenadores la que se usa es «Activar modificador de nivel».",
        ["liga.nivel100"] = "Pone a NIVEL 100 los 6 entrenadores de nivel más alto del juego (la liga), después de randomizar.",
        ["trainers.levelModified"] = "Activa el modificador de nivel de los entrenadores.",
        ["trainers.levelModifier"] = "PORCENTAJE que se aplica al nivel de los entrenadores: 40 = +40 % (nivel × 1,4). Se activa con la casilla «Activar modificador».",
        ["trainers.dificultadProgresiva"] = "Los entrenadores suben de nivel de forma progresiva según la zona.",
        ["trainers.equiposParejos"] = "Equipos más equilibrados y sin Pokémon repetidos.",
        ["trainers.campeonMasFuerte"] = "Refuerza el equipo del campeón.",
        ["trainers.megasALaAltura"] = "Da megapiedras a los entrenadores que pueden usarlas.",
        ["trainers.forceFullyEvolved"] = "Los entrenadores llevan Pokémon ya evolucionados a partir del nivel indicado.",
        ["trainers.forceFullyEvolvedLevel"] = "Nivel a partir del cual se fuerza la evolución.",
        ["trainers.allowAlternateForms"] = "Permite formas alternativas en los equipos.",
        ["trainers.blockLegendaries"] = "Impide legendarios en equipos normales.",
        ["trainers.forceMegaChampion"] = "El campeón usa megaevolución.",
        ["trainers.forceMegaEliteFour"] = "El Alto Mando usa megaevolución.",
        ["trainers.forceMegaGymLeaders"] = "Los líderes de gimnasio usan megaevolución.",
        ["trainers.forceMegaRivals"] = "El rival usa megaevolución.",
        ["trainers.swapMegaEvolvables"] = "Cambia Pokémon por otros que puedan megaevolucionar.",
        ["trainers.betterMovesets"] = "Mejora los movimientos de los entrenadores.",
        ["trainers.noEarlyWonderGuard"] = "Evita Superguarda en los primeros entrenadores.",
        ["trainers.randomizeNames"] = "Aleatoriza los nombres de los entrenadores.",
        ["trainers.randomizeClassNames"] = "Aleatoriza los nombres de las clases de entrenador.",
        ["trainers.rivalCarriesStarter"] = "El rival lleva el inicial que le corresponde.",
        ["trainers.leagueUnique"] = "El Alto Mando no repite Pokémon entre sus miembros.",
        ["trainers.leagueUniqueCount"] = "Cuántos Pokémon únicos garantiza el Alto Mando.",
        ["trainers.weightTypesByCount"] = "Elige tipos según cuántos Pokémon de ese tipo hay.",
        ["trainers.readjustToBstBand"] = "Ajusta cada equipo a la banda de fuerza de la zona.",
        ["trainers.shinyChanceOneIn"] = "Probabilidad de shiny en entrenadores (0 = desactivado; 4096 = 1 entre 4096).",
        ["trainers.heldItemsRegular"] = "Los entrenadores normales llevan objetos.",
        ["trainers.heldItemsImportant"] = "Los entrenadores importantes llevan objetos.",
        ["trainers.heldItemsBoss"] = "Los jefes llevan objetos.",
        ["trainers.heldItemsHighestLevelOnly"] = "Solo el Pokémon de nivel más alto lleva objeto.",
        ["trainers.heldItemsSensible"] = "Objetos coherentes con el tipo del Pokémon.",
        ["trainers.heldItemsConsumableOnly"] = "Solo objetos consumibles.",
        ["trainers.movesetForceGoodDamagingPercent"] = "Porcentaje mínimo de movimientos de daño garantizados.",
        ["trainers.gymLeaderBstLowerPercent"] = "Margen inferior de fuerza para los líderes de gimnasio.",
        ["trainers.gymLeaderBstUpperPercent"] = "Margen superior de fuerza para los líderes de gimnasio.",
        ["trainers.importantBstLowerPercent"] = "Margen inferior de fuerza para los entrenadores importantes.",
        ["trainers.importantBstUpperPercent"] = "Margen superior de fuerza para los entrenadores importantes.",
        ["trainers.legendLimited"] = "Limita el número de legendarios por equipo.",
        ["trainers.legendPerTeam"] = "Máximo de legendarios por equipo.",
        ["trainers.legendMinPerTeam"] = "Mínimo de legendarios por equipo.",
        ["trainers.legendAllowStrong"] = "Permite legendarios fuertes.",
        ["trainers.legendChampion"] = "El campeón puede llevar legendarios.",
        ["trainers.legendEliteFour"] = "El Alto Mando puede llevar legendarios.",
        ["trainers.legendLeadersFrom"] = "Desde qué gimnasio se permiten legendarios (0 = todos).",
        ["trainers.legendStrongWhereFits"] = "Usa legendarios fuertes solo donde encajen por nivel.",
        ["trainers.legendRivals"] = "El rival puede llevar legendarios.",
        ["trainers.additionalBoss"] = "Añade Pokémon extra a los jefes.",
        ["trainers.additionalBossCount"] = "Cuántos Pokémon extra añade a los jefes.",
        ["trainers.additionalImportant"] = "Añade Pokémon extra a los entrenadores importantes.",
        ["trainers.additionalImportantCount"] = "Cuántos Pokémon extra añade a los importantes.",
        ["trainers.additionalRegular"] = "Añade Pokémon extra a los entrenadores normales.",
        ["trainers.additionalRegularCount"] = "Cuántos Pokémon extra añade a los normales.",
        ["trainers.doubleBattleChanceBoss"] = "Probabilidad (%) de combate doble en jefes.",
        ["trainers.doubleBattleChanceImportant"] = "Probabilidad (%) de combate doble en importantes.",
        ["trainers.doubleBattleChanceRegular"] = "Probabilidad (%) de combate doble en normales.",

        // ── tutores ──
        ["tutorcompat.mod"] = "Qué movimientos puede enseñar cada Pokémon con los tutores.",
        ["tutorcompat.followEvolutions"] = "La compatibilidad con tutores sigue la línea evolutiva.",
        ["tutorcompat.levelupMoveSanity"] = "Evita combos incoherentes con lo que aprende por nivel.",
        ["tutormoves.mod"] = "Qué movimientos enseñan los tutores.",
        ["tutormoves.forceGoodDamaging"] = "Garantiza movimientos de daño útiles.",
        ["tutormoves.forceGoodDamagingPercent"] = "Porcentaje mínimo de movimientos de daño.",
        ["tutormoves.keepFieldMoves"] = "Mantiene los movimientos de campo.",
        ["tutormoves.noGameBreaking"] = "Evita movimientos que rompen el juego.",

        // ── tipos ──
        ["types.mod"] = "Qué hacer con los tipos de los Pokémon: sin cambios, aleatorios, etc.",
        ["types.forceDualTypes"] = "Fuerza a que todos tengan dos tipos.",

        // ── Pokémon salvajes ──
        ["wild.mod"] = "Qué hacer con los Pokémon salvajes: sin cambios, aleatorios, de zona temática…",
        ["wild.additionalRule"] = "Regla extra para los salvajes (por fuerza, por tipo de la zona, etc.).",
        ["wild.allowAlternateForms"] = "Permite formas alternativas entre los salvajes.",
        ["wild.balanceShakingGrass"] = "Equilibra el Pokémon de la hierba que se mueve.",
        ["wild.banBadItems"] = "Quita objetos inútiles a los salvajes.",
        ["wild.blockLegendaries"] = "Impide legendarios entre los salvajes.",
        ["wild.levelModifierValue"] = "PORCENTAJE aplicado al nivel de los Pokémon salvajes: 40 = +40 %.",
        ["wild.percentageLevelModifier"] = "El modificador de nivel es porcentaje.",
        ["wild.minimumCatchRateLevel"] = "Nivel a partir del cual se aplica la tasa de captura mínima.",
        ["wild.useMinimumCatchRate"] = "Aplica una tasa de captura mínima a los salvajes.",
        ["wild.randomizeHeldItems"] = "Aleatoriza el objeto que llevan los salvajes.",
        ["wild.shinyChanceOneIn"] = "Probabilidad de shiny salvaje (0 = desactivado; 4096 = 1 entre 4096).",
        ["wild.useTimeBasedEncounters"] = "Mantiene los encuentros que dependen de la hora del día.",
    };

    private static readonly Dictionary<string, string> PorCategoria = new(StringComparer.OrdinalIgnoreCase)
    {
        ["abilities"] = "Opciones de habilidades de los Pokémon.",
        ["basestats"] = "Estadísticas BASE de cada especie (la fuerza/velocidad del Pokémon, no los IVs/EVs de tu partida).",
        ["evolutions"] = "Opciones de evoluciones (condiciones y a qué evolucionan).",
        ["fielditems"] = "Opciones de los objetos repartidos por el mapa.",
        ["liga"] = "Nivel de la liga (Elite Four y Campeón).",
        ["misc"] = "Opciones varias del parche de calidad de vida del juego.",
        ["movedata"] = "Opciones de los datos de los movimientos (potencia, precisión, tipo…).",
        ["movesets"] = "Opciones de los movimientos que se aprenden subiendo de nivel.",
        ["pickup"] = "Opciones de la habilidad Recogida.",
        ["rom"] = "Datos internos de la ROM que se está randomizando.",
        ["shops"] = "Opciones de lo que venden las tiendas.",
        ["starters"] = "Opciones de los Pokémon iniciales.",
        ["statics"] = "Opciones de los Pokémon estáticos (legendarios, regalos, fósiles…).",
        ["tmcompat"] = "Opciones de qué MT/MO puede aprender cada Pokémon.",
        ["tmmoves"] = "Opciones de qué movimientos llevan las MT/MO.",
        ["totems"] = "Opciones de los tótems (Sol/Luna y Ultrasol/Ultraluna).",
        ["trades"] = "Intercambios internos: puedes aleatorizar SOLO lo que recibes (RANDOMIZE_GIVEN) o también lo que debes entregar.",
        ["trainers"] = "Equipos de los entrenadores. El modificador de nivel es ABSOLUTO (±niveles): el motor no admite porcentaje en entrenadores.",
        ["tutorcompat"] = "Opciones de qué movimientos enseñan los tutores a cada Pokémon.",
        ["tutormoves"] = "Opciones de qué movimientos enseñan los tutores.",
        ["types"] = "Opciones de los tipos de los Pokémon.",
        ["wild"] = "Opciones de los Pokémon salvajes.",
    };

    /// <summary>Texto del tooltip: qué hace la opción + su clave y valor actuales.</summary>
    public static string Para(string clave, string? valor)
    {
        var descripcion = Exactas.TryGetValue(clave, out var exacta)
            ? exacta
            : PorCategoria.TryGetValue(clave.Split('.')[0], out var categoria) ? categoria : "Opción del randomizador.";

        var valorLegible = valor switch
        {
            null => "—",
            "true" => "Sí",
            "false" => "No",
            _ when string.Equals(valor, "UNCHANGED", StringComparison.OrdinalIgnoreCase) => "sin cambios",
            _ => valor,
        };
        return descripcion + "\n\nClave: " + clave + "  ·  valor: " + valorLegible;
    }

    private static readonly Dictionary<string, string> Valores = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AREA_MAPPINGS"] = "Cada zona mapea sus Pokémon a otros, manteniendo la estructura por área.",
        ["GLOBAL_MAPPINGS"] = "Un único mapeo global para todos los encuentros.",
        ["SWAP_LEGENDARIES_SWAP_STANDARDS"] = "Intercambia legendarios entre sí y normales entre sí.",
        ["FULL_COMPATIBLE"] = "Todos los Pokémon pueden aprender todo (compatibilidad total).",
        ["RANDOM_EVEN_DISTRIBUTION"] = "Reparto aleatorio pero equilibrado por zonas.",
        ["RANDOM_EVERY_LEVEL"] = "Cambia la evolución en cada nivel (muy caótico).",
        ["METRONOME_ONLY"] = "Todos los movimientos son Metrónomo.",
        ["RANDOM_SAME_STRENGTH"] = "Aleatorio de la misma fuerza.",
        ["UNCHANGED"] = "No cambia nada en esta categoría.",
        ["RANDOM"] = "Cambia por otra opción al azar.",
        ["RANDOMIZE"] = "Aleatoriza los valores.",
        ["RANDOMIZE_BOTH"] = "Aleatoriza los dos lados.",
        ["SHUFFLE"] = "Mezcla los valores existentes entre especies o zonas.",
        ["COMPLETELY_RANDOM"] = "Cualquier Pokémon/movimiento del juego, sin filtros.",
        ["RANDOM_FOLLOW_EVOLUTIONS"] = "Aleatorio manteniendo la línea evolutiva.",
        ["RANDOM_SIMILAR_STRENGTH"] = "Aleatorio pero de fuerza parecida al original.",
        ["RANDOM_PREFER_SAME_TYPE"] = "Aleatorio prefiriendo el mismo tipo.",
        ["RANDOM_PREFER_TYPE"] = "Aleatorio prefiriendo un tipo.",
        ["RANDOM_FULLY"] = "Totalmente aleatorio.",
        ["UPDATE"] = "Actualiza a los datos de una generación posterior.",
        ["KEEP"] = "Mantiene el valor original.",
        ["RANDOM_WITH_TWO_EVOLUTIONS"] = "Iniciales que evolucionan dos veces, como los normales del juego.",
        ["CUSTOM"] = "Usa los iniciales personalizados (custom1, custom2 y custom3).",
        ["RANDOMIZE_GIVEN"] = "El Pokémon que RECIBES es aleatorio; tú entregas el que pida el juego (puede ser cualquiera).",
        ["RANDOMIZE_GIVEN_AND_REQUESTED"] = "Aleatoriza tanto lo que recibes como lo que debes entregar.",
        ["DISTRIBUTED"] = "Reparte los Pokémon por zonas según su fuerza.",
        ["MAINPLAYTHROUGH"] = "Solo cambia los combates de la historia principal.",
        ["GYMS"] = "Solo cambia los gimnasios.",
        ["TYPE_THEMED"] = "Equipos temáticos por tipo.",
        ["TYPE_THEMED_ELITE"] = "Alto Mando temático por tipo.",
        ["SAME_STRENGTH"] = "Misma fuerza que el original.",
        ["NONE"] = "Sin regla adicional.",
        ["SIMILAR_STRENGTH"] = "Parecido en fuerza.",
        ["CATCH_EM_ALL"] = "Todos los Pokémon disponibles en la zona.",
        ["TYPE_THEMED_AREAS"] = "Zonas temáticas por tipo.",
        ["LEGENDARIES"] = "Solo legendarios.",
        ["STRONG_LEGENDARIES"] = "Solo legendarios fuertes.",
        ["ALL"] = "Todos los Pokémon.",
        ["ERRATIC"] = "Curva errática (crece muy irregular).",
        ["FAST"] = "Curva rápida.",
        ["MEDIUM_FAST"] = "Curva media-rápida.",
        ["MEDIUM_SLOW"] = "Curva media-lenta.",
        ["SLOW"] = "Curva lenta.",
        ["FLUCTUATING"] = "Curva fluctuante.",
    };

    /// <summary>Explicación del VALOR seleccionado en un desplegable (no de la clave).</summary>
    public static string ParaValor(string clave, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "Elige un valor.";
        if (Valores.TryGetValue(valor, out var texto)) return texto;
        if (int.TryParse(valor, out _)) return "Cantidad numérica de esta opción.";
        return "Valor del motor: " + valor;
    }

    /// <summary>Descripción de la categoría (para la cabecera de opciones).</summary>
    public static string Categoria(string prefijo)
        => PorCategoria.TryGetValue(prefijo, out var texto) ? texto : "";

    /// <summary>
    /// Valores VÁLIDOS de cada desplegable según el motor (extraídos de SettingsProperties.java).
    /// Si no están en esta tabla, el motor ignora el valor y usa el primero (UNCHANGED), que es
    /// exactamente lo que pasaba con «COMPLETELY_RANDOM» en habilidades.
    /// </summary>
    public static readonly Dictionary<string, string[]> ValoresMotor = new(StringComparer.OrdinalIgnoreCase)
    {
        ["types.mod"] = ["UNCHANGED", "RANDOM_FOLLOW_EVOLUTIONS", "COMPLETELY_RANDOM"],
        ["abilities.mod"] = ["UNCHANGED", "RANDOMIZE"],
        ["basestats.mod"] = ["UNCHANGED", "SHUFFLE", "RANDOM"],
        ["basestats.expCurveTarget"] = ["LEGENDARIES", "STRONG_LEGENDARIES", "ALL"],
        ["starters.mod"] = ["UNCHANGED", "CUSTOM", "COMPLETELY_RANDOM", "RANDOM_WITH_TWO_EVOLUTIONS"],
        ["statics.mod"] = ["UNCHANGED", "SWAP_LEGENDARIES_SWAP_STANDARDS", "COMPLETELY_RANDOM", "RANDOM_SIMILAR_STRENGTH"],
        ["trades.mod"] = ["UNCHANGED", "RANDOMIZE_GIVEN", "RANDOMIZE_BOTH"],
        ["evolutions.mod"] = ["UNCHANGED", "RANDOM", "RANDOM_EVERY_LEVEL"],
        ["movesets.mod"] = ["UNCHANGED", "RANDOM_PREFER_SAME_TYPE", "COMPLETELY_RANDOM", "METRONOME_ONLY"],
        ["trainers.mod"] = ["UNCHANGED", "RANDOM", "DISTRIBUTED", "MAINPLAYTHROUGH", "TYPE_THEMED", "TYPE_THEMED_ELITE4_GYMS"],
        ["totems.mod"] = ["UNCHANGED", "RANDOM", "RANDOM_SIMILAR_STRENGTH"],
        ["totems.allyMod"] = ["UNCHANGED", "RANDOM", "RANDOM_SIMILAR_STRENGTH"],
        ["totems.auraMod"] = ["UNCHANGED", "RANDOM", "RANDOM_SAME_STRENGTH"],
        ["wild.mod"] = ["UNCHANGED", "RANDOM", "AREA_MAPPINGS", "GLOBAL_MAPPINGS"],
        ["wild.additionalRule"] = ["NONE", "SIMILAR_STRENGTH", "CATCH_EM_ALL", "TYPE_THEMED_AREAS"],
        ["tmmoves.mod"] = ["UNCHANGED", "RANDOM"],
        ["tmcompat.mod"] = ["UNCHANGED", "RANDOM_PREFER_TYPE", "COMPLETELY_RANDOM", "FULL_COMPATIBLE"],
        ["tutormoves.mod"] = ["UNCHANGED", "RANDOM"],
        ["tutorcompat.mod"] = ["UNCHANGED", "RANDOM_PREFER_TYPE", "COMPLETELY_RANDOM", "FULL_COMPATIBLE"],
        ["fielditems.mod"] = ["UNCHANGED", "SHUFFLE", "RANDOM", "RANDOM_EVEN_DISTRIBUTION"],
        ["shops.mod"] = ["UNCHANGED", "SHUFFLE", "RANDOM"],
        ["pickup.mod"] = ["UNCHANGED", "RANDOM"],
    };

    /// <summary>¿El motor admite este valor para esta clave?</summary>
    public static bool ValorValido(string clave, string valor)
        => !ValoresMotor.TryGetValue(clave, out var validos)
           || validos.Any(v => string.Equals(v, valor, StringComparison.OrdinalIgnoreCase));
}
