## Models

### Movement

- [x] Energy cost based on terrain (per-terrain drain per second, tunable in the GridManager Inspector)
- [x] Movement speed based on terrain (per-terrain speed multiplier)
- [x] Slower movement at low energy
- [x] Random-walk patrol with a random 0.3 to 3 s pause between destinations
- [x] Rigidbody-based movement (gravity and collision, speed controlled by code)
- [x] Cold temperature: slower on all tiles and higher energy cost
- [ ] Controlling movement when the model is selected with WASD
- [ ] Swimming: non-swimmers can learn to swim at an energy cost
- [ ] Hot temperatures: extra energy cost, dehydration, health damage
- [ ] Water temperature affects swimming (energy cost, health)

### Gender, Mating, Roles

- [ ] Apply gender (field exists, no behavior yet)
- [ ] Apply mating (`wantsMate` field exists, no behavior yet)
- [ ] Apply roles: workers, hunters, builders (field exists, no behavior yet)

### Personalities (enum exists, no behavior yet)

- [ ] Lazy
- [ ] Greedy
- [ ] Thief
- [ ] Adventurer
- [ ] Personality-specific eating: Greedy eats even when full, Lazy takes the nearest, Thief steals

### Sleep

- [x] Instant sleep at 0 energy, voluntary sleep when energy is low
- [x] Sleep restores energy (60 s from empty to full on ideal terrain)
- [x] Sleep quality: sleeping tile quality
- [x] Sleep quality: temperature (global cold)
- [x] No sleeping in water: out of energy in water means passing out, losing health, and dying at 0 health
- [ ] Local cold spots and hot temperatures affect sleep quality
- [ ] If low on energy in water, head for the nearest shore
    - [ ] If another model that can swim is nearby, it comes to help (rescue a passed-out model)

### Health

- [ ] Low hunger drains energy faster and decreases health
- [ ] Other health changes from dangers
- [ ] Extreme cold damage (hypothermia)
### Death

- [x] Model dies at 0 health and stops moving
- [ ] Corpse remains on the ground
- [ ] If not found, the corpse decomposes (rate depends on temperature)
- [ ] If found, the community is notified
    - [ ] The community buries the corpse

### Sight

- [x] Sight range (15 cells by default)
- [ ] Sight drops in the dark and at night (7 or 5 m)
- [ ] 

### Behaviors

- [ ] Food preference
- [ ] Racism!
- [ ] Systematics
- [ ] Individualists
- [ ] 

---

## Food

- [x] Hunger decreases while moving
- [x] Eating increases hunger and gives a little energy
- [x] A hungry model goes after the nearest food in sight, otherwise keeps patrolling
- [x] Random food spawning (5 food on 5 different cells every 60 s)
- [ ] Variations
    - [ ] Cherry
    - [ ] Apples
    - [ ] Fish
        - [ ] Spawns in the water
        - [ ] Fishing
    - [ ] Deer (hunting)
- [ ] Rotting over time

---

## Temperature

- [x] Global temperature (separate TemperatureManager script)
- [x] Cold effects: worse sleep quality, slower movement, higher energy cost
- [ ] Food rots slower in the cold and faster in the heat
- [ ] Corpses decompose slower in the cold
- [ ] Hot temperature effects
- [ ] Water temperature (separate from air temperature)
- [ ] Water freezes in low temperatures, forming frozen lakes, rivers and seas
- [ ] Temperature variations
    - [x] Normal
    - [x] Cold (partly: effects exist, no named state)
    - [ ] Freezing
    - [ ] Hot
    - [ ] Melting
- [ ] Temperature changes with time of day, weather and local cold spots

---

## Weather

- [ ] Weather system (hot, cold, rainy, snowy, windy)
- [ ] Rain lowers temperature, makes terrain muddy, and can cause floods that create new lakes or rivers (from the GDD)

---

## Grid Manager

- [x] 2D array as the source of truth, with terrain and walkability queries
- [x] Out-of-bounds cells are always impassable
- [x] Map editing in the Inspector: hardcoded test patches, seeded random scatter, custom patch list
- [x] Spawns one tile per grid cell, with a tile prefab for each terrain (fallback colored cubes if empty)
    - [ ] Plain (green) tile art or texture
    - [ ] Muddy tile art or texture
    - [ ] Stony tile art or texture
    - [ ] Water tile art or texture
- [x] One ground collider for the whole grid (no per-tile colliders)
- [ ] Confirm out-of-bounds should always be impassable, since the GDD says water can be a border _(TODO: GridManager.cs)_
- [ ] Frozen water terrain state with its own tile
- [ ] `SetTerrain(cx, cz, type)` to change a single cell and its tile _(idea, needed for the admin panel)_
- [ ] Managing the grid via the admin panel

---

## Admin Panel

Groundwork already in code: `FoodSpawner.SpawnAtCell / SpawnAtWorld / SpawnRandom`, `TemperatureManager.SetGlobalTemperature`, and the map seed in the GridManager Inspector.

- [ ] Food spawn
    - [ ] Choose food variation
    - [ ] Choose amount
    - [ ] Click on the map or a location to spawn
- [ ] Map
    - [ ] 2D map view
    - [ ] Model locations on the map
    - [ ] Interactions
        - [ ] Clicking a model zooms in on it
        - [ ] Clicking the map while spawning food spawns it at that location
- [ ] Grid manager
    - [ ] Map size
    - [ ] Seed changer
- [ ] Temperature
    - [ ] Adjust temperature globally

---

## Other ideas and GDD items not scheduled yet

- [ ] Shelter and warmth sources (campfires, houses) as local warm spots
- [ ] Cold increases hunger drain
- [ ] On-screen debug readout for temperature and model stats
- [ ] Materials: wood, stone, iron for builders and tools (from the GDD)
- [ ] Water placing system to change borders or create challenges (from the GDD)
- [ ] Replace the rule-based behavior with trained AI (long-term goal)