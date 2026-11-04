CHARACTER LAB PLAYER: Dragon
============================================================

HOW TO USE IT (Unity 6 or newer)
1. Unzip this file and drag the "CharacterLab" folder from it into your Unity
   project's Assets folder (the Project window), so you get Assets/CharacterLab/...
   (Windows' "Extract All" puts it inside a "CharacterLab_Dragon" folder - dragging
   that whole folder in works too.) Let Unity finish importing.
2. Open the CharacterLab/Characters folder and drag "Dragon" into your scene.
   Put it on top of a floor. (No floor yet? GameObject > Character Lab > Test Ground.)
3. Press Play. That's it - the controls, camera, animations, effects and
   sounds all set themselves up. The player takes over your Main Camera.

CONTROLS
  Move ............ WASD or arrow keys
  Look ............ Mouse moves camera (cursor locked)
  Jump ............ Space
  Sprint .......... Left Shift (hold)
  Crouch / slide .. Left Ctrl (in the air: ground pound)
  Dash ............ E
  Action .......... Left Mouse
  Esc ............. let go of the mouse (click the game to grab it again)
  Gamepads work too: left stick move, right stick look, A jump, B crouch,
  X dash, Y action, RB grapple, LB sprint.

CHANGING IT
  Select the player in your scene and look at "Character Lab Player" in the
  Inspector. Every setting from the lab is there (hover a name for help).
  You can change them while the game is playing and see the result right
  away (Unity puts them back when you stop playing - change them again after
  stopping to keep them).
  - Model Override: drag any model here to use it instead.
  - Player Camera: leave empty to use the Main Camera.
  - Sounds live in Assets/CharacterLab/Resources/CharacterLab/Sounds - swap
    the .wav files for your own (keep the names).

EXTRAS FOR YOUR LEVELS (GameObject > Character Lab menu)
  Bounce Pad, Moving Platform, Grapple Point, Test Ground.

FROM YOUR OWN SCRIPTS
  var player = FindAnyObjectByType<CharacterLab.CharacterLabPlayer>();
  player.Launch(new Vector3(0, 20, 0));   // throw the player
  player.Teleport(somePosition);           // move instantly
  player.SpawnPoint = checkpointPosition;  // where you respawn after a fall
  player.Jumped += () => Debug.Log("Jump!"); // also: AirJumped, WallJumped, Dashed,
                                            // Landed, GroundPounded, Respawned

GOOD TO KNOW
  - Uses Unity's new Input System (the default in Unity 6). If your project
    only has the old Input Manager turned on, it uses that automatically.
  - Works with URP and the Built-in render pipeline.
  - Adding another Character Lab player to the same project is fine (one at a
    time): its copy of the shared scripts merges with the one already there,
    even when it lands in its own folder, and both characters stay.
  - Red errors saying something "already contains a definition for" a Character
    Lab script? Two copies of the scripts arrived at the same time. Delete the
    Runtime and Editor folders inside ONE of your CharacterLab folders.

Built-in models by Quaternius (quaternius.com) and Kenney (kenney.nl) are CC0 (free to use for anything).
