# Try the Stoat hitching post

1. Install the two ZIPs in `mods/` by extracting their enclosing folders into your own `Kenshi/mods` folder. Enable **Meitou Husbandry → Stoat Hitching Post** in packaged **MeitouClient v0.22.1, Windows x64**. Allow the framework code mod when prompted.
2. Copy `saves/Two Garru outside Stoat` to your Meitou save directory and load it. This is an isolated test save with a handler and two owned Garru. The post is outside the gate at **35071, -70668**.
3. Select the first Garru and use **F10 → Assign to nearby hitching post**. Repeat to confirm the assignment. Try the second Garru: the post is occupied.
4. Walk the handler away. Select the assigned Garru and order it away from the post. Allow the order to finish and watch the idle animal return.
5. A stock gate guard may inspect the handler's belongings. Answer **Okay, go ahead...**, then **[Leave]**. Conversations pause the game normally.
6. Save while assigned, reload, and check that the same animal remembers the post. The other Garru should remain unassigned.
7. During another return use **Release from hitching post**. Order that Garru away again; it should stay away. The other Garru can now use the post.

The separate assigned fixture save captures the tested assignment. Keep test saves separate from your usual playthrough.

Report the game version, mod load order, animal/post identity and shortest reproduction at https://github.com/Meitou-Modding-Ecosystem/MeitouHusbandry/issues. Include a save only if you are comfortable sharing it. The mod supplies no ropes, tether animation, breeding or taming in this playtest.
