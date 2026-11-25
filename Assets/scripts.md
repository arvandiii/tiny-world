# 01 Implementing GridManager
first, we're going to make the foundation for the game, a grid manager for further implementations and a tile type property to declare what sort of grounds we have in game.
we're going to have a plane tile, a muddy tile, water and stony tiles for different ground types.
since its only the start, we hard coded the tiles to just see how it will look. lets turn the gizmo on again and boom, we have a adjustable grid, we can adjust the Length and width, along with the cell sizes, debug preview and the locations of where the grid starts. and here we go.

# 02 Editing the map
ok back on the grid manager, we implemented a new feature, which is an adjustable tile system, so we no longer need to hard code the tiles. we can use the default grid that we implemented before, or we can just use a seed and change the amount of each tile type in the game.
we also added a custom patch field so we can use in the future for some made up maps, and we're done.

# 03 Tiles and ground
in this video we're going to make the tiles we declared to have a visual map. we added a quad, rotated it to face up, made a few of them to match the types in tile type we declared, make them all prefabs to use them later in the code. we also found some free images to implement the tiles type, plane, muddy, stony and water. lets just change its color to blue, and this one to brown. after implementing the code, adding the tile references to our grid manager. oops, i forgot to save the prefabs. lets run it, and boom, this are for early tests, we will change the tiles later to make them look better. but hey, its done!

# 04 Model
in this video we're going to make our first model. nothing serious at first, just some pre-written behaviors. lets attach the script to the model and open it. lets paste the code and see how it will look. lets place it on the grid so it knows where to go. a little but higher, and press run.
ok, the model is moving around, we can see it chooses its destination and goes towards it. we can also change its movement speed, how long can it see to choose its next destination. and if we click on the swim checkbox it will cross on the water tiles as well. and that's it, our very first model

# 05 Pause and facing
lets give our model some sort of a face, to notice where is it looking at.
a cube would do.
that's it.
let me replace the code with the new one real quick...
these are the new variables, a range of numbers for how long the model should wait before heading towards a new destination.
lets test it.
oops, wrong way.
there you go.
lets test it again and boom, the model faces its destination

# 06 Terrain speed
the plan was that the model walk on each terrain type with a different speed, so lets implement that!
let me do the boring coding part first...
the way we designed it that each tile has a multiplier that make the speed variant. its walking fast on the plane tiles, and on the stony tiles it slows down. alright, thats it!