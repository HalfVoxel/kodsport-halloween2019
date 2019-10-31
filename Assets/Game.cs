#pragma warning disable 649
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Pathfinding.Serialization;
using Pathfinding;
using UnityEngine.Tilemaps;
using System.Linq;
using UnityEngine.UI;
using System.Text.RegularExpressions;
using UnityEngine.Profiling;

public class Game : MonoBehaviour
{
	public Tilemap tilemap;
	public Tilemap tilemap2;
	public TileBase[] tiles;
	public Text eventLog;
	public GameObject player;
	public GameObject prefabPlayer;
	public GameObject prefabPlayerUI;
	public GameObject prefabItemUI;
	public GameObject prefabEquippedItemUI;
	public Camera cam;
	public Transform rootEntityUI;
	public Transform rootItems;
	public Transform rootEquippedItems;
	public PrefabTilemap prefabTilemap;
	public Camera minimapCamera;
	public UnityEngine.Experimental.Rendering.Universal.Light2D globalLight;
	public Text levelText;
	public SfxHolder[] soundEffects;
	public Text statusLabel;
	static readonly string apiKey = "ce81093e-98b3-4314-ae8c-0291557443a6";
	static readonly string playerName = "voxelbot";

	static Dictionary<HalloweenBot.Status, List<string>> statusTexts = new Dictionary<HalloweenBot.Status, List<string>> {
		{ HalloweenBot.Status.LookingForLadder, new List<string>
			{
				"Hmmm... I wonder if there's a ladder somewhere here",
				"Where could the ladder be??",
				"Surely the ladder must be in this room"
			}
		},
		{ HalloweenBot.Status.FleeingFromIT, new List<string>
			{
				"Aaaah! It's a scary thing!",
				"What the hell is that!?",
				"Oh no! It's an IT, I saw a movie about one of those once!",
			}
		},
		{ HalloweenBot.Status.Fleeing, new List<string>
			{
				"Aaaah! It's a scary monster!",
				"Got to get more gear before fighting those things!",
				"Scary! Better get far away from here!"
				// "It's only a flesh wound... it hurts though",
			}
		},
		{ HalloweenBot.Status.LookingForMonsters, new List<string>
			{
				"I'm a monster hunter and I'm OK! I kill all night and I kill all day!",
				"Come out where I can see you!",
				"Where aaaare you?",
				"This is a dungeon, one would think there aught to be some monsters here",
			}
		},
		{ HalloweenBot.Status.MovingToLadder, new List<string>
			{
				"I know where the next level is!",
				"Let's see, I think I remember the way now",
			}
		},
		{ HalloweenBot.Status.AttackingMonster, new List<string>
			{
				"Chaaaarge!",
				"Die! You evil monster!",
				"A dead monster is a good monster!",
				"Have at you!",
				"Prepare to die!",
				"I found a monster! Let's kill it!",
			}
		},
		{ HalloweenBot.Status.Idle, new List<string>
			{
				"I don't know what to do!?",
			}
		},
	};

	public class RawObservation {
		public string[] itemNames;
		public string[] surrounding;
		public int radius;
		public RawEntity[] players;
		public RawEntity[] monsters;
		public string[] equipped;
		public string error;
		public string[] events;
	}

	public struct Pos {
		public int row, col;
	}

	public class RawEntity {
		public EntityType type;
		public Pos relativePos;
		public string name;
		public float health;
	}

	[System.Serializable]
	public class SfxHolder {
		public Sfx type;
		public AudioClip[] clips;
		public string name;

		public IEnumerator Play () {
			var go = new GameObject("OneShotAudio");
			var source = go.AddComponent<AudioSource>();
			var clip = clips[Random.Range(0, clips.Length - 1)];
			source.PlayOneShot(clip);
			source.spatialBlend = 0.0f;
			yield return new WaitForSeconds(clip.length + 1);
			GameObject.Destroy(go);
			//AudioSource.PlayClipAtPoint(, Camera.main.transform.position);
		}
	}

	public enum Sfx {
		Step,
		DrinkPotion,
		EquipArmor,
		EquipWeapon,
		PlayerDied,
		FellDownLadder,
		AttackSword,
		AttackMagic,
		AttackHands,
		Idle,
	}

	public enum CellState {
		Free,
		Occupied,
		Unknown,
		Error,
	}

	public enum Direction {
		North,
		East,
		South,
		West,
	}

	public static readonly Int2[] Direction2delta = {
		new Int2(0, -1),
		new Int2(1, 0),
		new Int2(0, 1),
		new Int2(-1, 0),
	};

	[System.Serializable]
	public class PrefabTilemap {
		public bool enabled = true;
		GameObject[] tiles = new GameObject[1];
		IntRect bounds;
		public GameObject prefab;
		public Transform root;

		public void Expand(IntRect newBounds) {
			newBounds = IntRect.Union(bounds, newBounds);
			if (bounds == newBounds) return;

			var relativeBounds = bounds.Offset(new Int2(-newBounds.xmin, -newBounds.ymin));
			GameObject[] newCells = new GameObject[newBounds.Height*newBounds.Width];

			// Copy old bounds
			for (int y = 0; y < relativeBounds.Height; y++) {
				for (int x = 0; x < relativeBounds.Width; x++) {
					newCells[(y + relativeBounds.ymin)*newBounds.Width + (x + relativeBounds.xmin)] = tiles[y*bounds.Width + x];
				}
			}

			tiles = newCells;
			bounds = newBounds;
		}

		public void Set(Int2 p, bool occupied) {
			if (!enabled) return;
			if (!bounds.Contains(p.x, p.y)) Expand(new IntRect(p.x, p.y, p.x, p.y));

			var go = tiles[(p.y - bounds.ymin) * bounds.Width + (p.x - bounds.xmin)];
			if(go == null && occupied) {
				go = GameObject.Instantiate(prefab, root);
				go.transform.position = new Vector2(p.x, p.y);
			} else if (go != null && !occupied) {
				GameObject.Destroy(go);
				go = null;
			}
			tiles[(p.y - bounds.ymin) * bounds.Width + (p.x - bounds.xmin)] = go;
		}
	}

	public class Observation {
		public CellState[,] surrounding;
		public int radius;
		public RawEntity[] players;
		public RawEntity[] monsters;
		public string[] equipped;
		public string[] itemNames;
		public string error;
		public string[] events;

		public Observation(RawObservation observation) {
			UnityEngine.Assertions.Assert.IsTrue(observation.surrounding[0].Length == observation.radius*2 + 1);
			surrounding = new CellState[observation.surrounding.Length, observation.surrounding.Length];
			for (int y = 0; y < observation.surrounding.Length; y++) {
				string line = observation.surrounding[y];
				for (int x = 0; x < line.Length; x++) {
					switch(line[x]) {
						case '#':
							surrounding[y,x] = CellState.Occupied;
							break;
						case '.':
							surrounding[y,x] = CellState.Free;
							break;
						default:
							surrounding[y,x] = CellState.Error;
							break;
					}
				}
			}

			radius = observation.radius;
			players = observation.players;
			monsters = observation.monsters;
			foreach (var e in monsters) e.type = EntityType.Monster;
			foreach (var e in players) e.type = EntityType.Player;
			equipped = observation.equipped;
			itemNames = observation.itemNames;
			error = observation.error;
			events = observation.events ?? new string[0];
		}

		public bool HasEvent(string evName) {
			if (events == null) return false;
			return events.Contains(evName);
		}
	}

	public enum EntityType {
		Player,
		Monster,
	}

	public class Entity {
		public EntityType type;
		public Int2 pos;
		public Vector2 interpolatedPos;
		public string name;
		public GameObject go;
		public GameObject goUI;
		public int lastSeenFrame = -1;
		public float health;
		Game game;
		Image healthBarSlider;
		public float maxSeenHealth = 0;

		public Entity (Int2 pos, string name, EntityType type, int frame) {
			this.pos = pos;
			this.name = name;
			this.type = type;
			this.interpolatedPos = new Vector2(pos.x, pos.y);
			this.lastSeenFrame = frame;
		}

		public void Update () {
			maxSeenHealth = Mathf.Max(maxSeenHealth, health);
			maxSeenHealth = Mathf.Lerp(maxSeenHealth, health, 0.2f * Time.deltaTime);

			interpolatedPos = Vector2.MoveTowards(interpolatedPos, new Vector2(pos.x, pos.y), Time.deltaTime * 2f);
			go.transform.position = interpolatedPos;
			if (name == playerName) {
				game.cam.transform.position = (Vector3)interpolatedPos + new Vector3(0, 0, -10);
			}
			goUI.transform.position = game.cam.WorldToScreenPoint(interpolatedPos);
			healthBarSlider.fillAmount = maxSeenHealth > 0 ? health / maxSeenHealth : 0;
			var dir = interpolatedPos - new Vector2(pos.x, pos.y);
			if (dir.magnitude > 0) {
				go.transform.eulerAngles = new Vector3(0, 0, Mathf.Rad2Deg * Mathf.Atan2(dir.y, dir.x) + 90); 
			}
		}

		public void Spawn (Game game) {
			this.game = game;
			if (name == playerName) {
				go = game.player;
			} else {
				go = GameObject.Instantiate(game.prefabPlayer, interpolatedPos, Quaternion.identity);
				go.transform.SetParent(game.transform, true);
			}

			goUI = GameObject.Instantiate(game.prefabPlayerUI, game.rootEntityUI);
			goUI.GetComponentInChildren<Text>().text = name;
			healthBarSlider = goUI.transform.Find("Bar/Health").GetComponent<Image>();
		}

		public void OnDeath(Game game) {
			GameObject.Destroy(go);
			GameObject.Destroy(goUI);
		}
	}

	public class Item {
		public bool equipped;
		public string name;
		Game game;
		GameObject go;
		public int lastSeenFrame = -1;

		public void Spawn(Game game) {
			this.game = game;
			go = GameObject.Instantiate(game.prefabItemUI, game.rootItems);
			go.transform.Find("Equip").GetComponent<Button>().onClick.AddListener(Use);
			go.transform.Find("Label").GetComponent<Text>().text = name;
			OnUnequipped();
		}

		public void Use () {
			Debug.Log("Trying to use " + name);
			game.Use(this, () => {
				OnEquipped();
			});
		}

		public void OnLost() {
			GameObject.Destroy(go);
		}

		public void OnEquipped () {
			equipped = true;
			go.transform.Find("Equip").gameObject.SetActive(false);
			go.transform.Find("Equipped").gameObject.SetActive(true);
			go.transform.SetAsLastSibling();

			switch(HalloweenBot.Classify(this)) {
				case HalloweenBot.EquipmentTag.Breastplate:
				case HalloweenBot.EquipmentTag.Helmet:
				case HalloweenBot.EquipmentTag.Legplates:
					game.PlaySoundEffect(Sfx.EquipArmor);
					break;
				case HalloweenBot.EquipmentTag.Weapon:
					game.PlaySoundEffect(Sfx.EquipWeapon);
					break;
				case HalloweenBot.EquipmentTag.Potion:
					game.PlaySoundEffect(Sfx.DrinkPotion);
					break;
			}
		}

		public void OnUnequipped () {
			equipped = false;
			go.transform.Find("Equip").gameObject.SetActive(true);
			go.transform.Find("Equipped").gameObject.SetActive(false);
		}
	}

	public enum Knowledge {
		Unknown,
		Historical,
		Known,
	}

	public struct HistoricalValue<T> {
		public Knowledge knowledge;
		public T value;

		public void TryApply(HistoricalValue<T> other) {
			if (other.knowledge > knowledge) {
				this = other;
			}
		}
	}

	public struct Cell {
		public HistoricalValue<CellState> state;
		public HistoricalValue<bool> visited;
		public HistoricalValue<bool> ladder;
	}

	enum MissingAction {
		Keep,
		Remove,
	}

	static void Match<A, B>(List<A> gameItems, List<B> observedItems, System.Func<A, B, float?> matchCost, System.Func<B, A> onCreate, System.Action<A, B> onUpdate, System.Func<A, MissingAction> onMissing) {
		List<System.Tuple<float, B, A>> distances = new List<System.Tuple<float, B, A>>();
		foreach (var obsEntity in observedItems) {
			foreach (var gameEntity in gameItems) {
				var cost = matchCost(gameEntity, obsEntity);
				if (cost != null) {
					distances.Add(System.Tuple.Create(cost.Value, obsEntity, gameEntity));
				}
			}
		}

		distances.Sort((x, y) => x.Item1.CompareTo(y.Item1));
		List<A> matchedGameEntities = new List<A>();
		List<B> matchedEntities = new List<B>();
		foreach (var dist in distances) {
			if (!matchedGameEntities.Contains(dist.Item3) && !matchedEntities.Contains(dist.Item2)) {
				matchedEntities.Add(dist.Item2);
				matchedGameEntities.Add(dist.Item3);
				onUpdate(dist.Item3, dist.Item2);
			}
		}

		foreach (var obsEntity in observedItems) {
			if (!matchedEntities.Contains(obsEntity)) {
				var gameEntity = onCreate(obsEntity);
				gameItems.Add(gameEntity);
				matchedGameEntities.Add(gameEntity);
				matchedEntities.Add(obsEntity);
				onUpdate(gameEntity, obsEntity);
			}
		}

		for (int i = gameItems.Count - 1; i >= 0; i--) {
			var gameEntity = gameItems[i];
			if (!matchedGameEntities.Contains(gameEntity)) {
				if (onMissing(gameEntity) == MissingAction.Remove) {
					gameItems.RemoveAt(i);
				}
			}
		}

	}

	public class SeenEntity {
		public int frame;
		public Entity entity;
	}

	[JsonOptIn]
	public class Map {
		[JsonMember]
		Cell[] cells = new Cell[0];

		[JsonMember]
		public IntRect bounds = new IntRect(0, 0, -1, -1);

		Map expectedMap;
		Int2 expectedMapOffset;
		public int level;
		int[] hashes;
		int version;
		public List<int> levelVote = new List<int>();
		public List<int> levelVoteKeep = new List<int>();
		public Dictionary<Int2, SeenEntity> units = new Dictionary<Int2, SeenEntity>();

		public bool HasAnyInfoAbout(Int2 p) {
			return bounds.Contains(p.x, p.y) || (expectedMap != null && expectedMap.HasAnyInfoAbout(p));
		}

		public CellState GetState(Int2 p) {
			return GetState(p.x, p.y);
		}

		public ref Cell GetMut(Int2 p) {
			return ref GetMut(p.x, p.y);
		}

		public CellState GetState(int x, int y) {
			return Get(x, y).state.value;
		}

		public void ClearHistorical () {
			matchedMaps.Clear();
			knownBadOffsets.Clear();
			levelVote.Clear();
			for (int i = 0; i < cells.Length; i++) {
				if (cells[i].state.knowledge == Knowledge.Historical) cells[i].state = new HistoricalValue<CellState> { knowledge = Knowledge.Unknown, value = CellState.Unknown };
				if (cells[i].ladder.knowledge == Knowledge.Historical) cells[i].ladder = new HistoricalValue<bool> { knowledge = Knowledge.Unknown, value = false };
				if (cells[i].visited.knowledge == Knowledge.Historical) cells[i].visited = new HistoricalValue<bool> { knowledge = Knowledge.Unknown, value = false };
			}
			UpdateVotes();
		}

		void Trim() {
			Int2 mn = new Int2(bounds.xmax, bounds.ymax);
			Int2 mx = new Int2(bounds.xmin, bounds.ymin);
			for (int y = bounds.ymin; y <= bounds.ymax; y++) {
				for (int x = bounds.xmin; x <= bounds.xmax; x++) {
					var cell = GetMut(x, y);
					if (cell.state.knowledge != Knowledge.Unknown || cell.ladder.knowledge != Knowledge.Unknown || cell.visited.knowledge != Knowledge.Unknown) {
						mn = Int2.Min(mn, new Int2(x, y));
						mx = Int2.Max(mx, new Int2(x, y));
					}
				}
			}

			var newBounds = new IntRect(mn.x, mn.y, mx.x, mx.y);
			var newCells = new Cell[newBounds.Width*newBounds.Height];
			for (int y = newBounds.ymin; y <= newBounds.ymax; y++) {
				for (int x = newBounds.xmin; x <= newBounds.xmax; x++) {
					newCells[(y - newBounds.ymin)*newBounds.Width + (x - newBounds.xmin)] = GetMut(x, y);
				}
			}

			cells = newCells;
			bounds = newBounds;
		}

		public byte[] Serialize () {
			var output = new System.IO.MemoryStream();
			System.IO.BinaryWriter writer = new System.IO.BinaryWriter(output);
			writer.Write(5);
			writer.Write(level);
			writer.Write(bounds.xmin);
			writer.Write(bounds.ymin);
			writer.Write(bounds.xmax);
			writer.Write(bounds.ymax);
			writer.Write(cells.Length);
			for (int i = 0; i < cells.Length; i++) {
				writer.Write((int)cells[i].state.knowledge);
				writer.Write((int)cells[i].state.value);
				writer.Write((int)cells[i].ladder.knowledge);
				writer.Write(cells[i].ladder.value);
				writer.Write((int)cells[i].visited.knowledge);
				writer.Write(cells[i].visited.value);
			}
			writer.Write(levelVoteKeep.Count);
			for (int i = 0; i < levelVoteKeep.Count; i++) writer.Write(levelVoteKeep[i]);
			return output.ToArray();
		}

		public void Deserialize (byte[] bytes) {
			var input = new System.IO.MemoryStream(bytes);
			var reader = new System.IO.BinaryReader(input);
			version = reader.ReadInt32();
			level = reader.ReadInt32();
			bounds = new IntRect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
			cells = new Cell[reader.ReadInt32()];
			for (int i = 0; i < cells.Length; i++) {
				if (version == 0) {
					cells[i] = new Cell {
						state = new HistoricalValue<CellState> { knowledge = Knowledge.Historical, value = (CellState)reader.ReadInt32() },
						ladder = new HistoricalValue<bool> { knowledge = Knowledge.Historical, value = reader.ReadBoolean() },
						visited = new HistoricalValue<bool> { knowledge = Knowledge.Historical, value = reader.ReadBoolean() },
					};
					if (cells[i].state.value == CellState.Unknown) {
						cells[i].state.knowledge = Knowledge.Unknown;
						cells[i].ladder.knowledge = Knowledge.Unknown;
						cells[i].visited.knowledge = Knowledge.Unknown;
					}
				} else {
					var cell = new Cell {
						state = new HistoricalValue<CellState> { knowledge = (Knowledge)reader.ReadInt32(), value = (CellState)reader.ReadInt32() },
						ladder = new HistoricalValue<bool> { knowledge = (Knowledge)reader.ReadInt32(), value = reader.ReadBoolean() },
						visited = new HistoricalValue<bool> { knowledge = (Knowledge)reader.ReadInt32(), value = reader.ReadBoolean() },
					};
					if (cell.state.knowledge == Knowledge.Historical) cell.state = new HistoricalValue<CellState> { knowledge = Knowledge.Unknown, value = CellState.Unknown };
					else if (cell.state.knowledge != Knowledge.Unknown) cell.state.knowledge = Knowledge.Historical;
					if (cell.ladder.knowledge == Knowledge.Historical) cell.ladder = new HistoricalValue<bool> { knowledge = Knowledge.Unknown, value = false };
					else if (cell.ladder.knowledge != Knowledge.Unknown) cell.ladder.knowledge = Knowledge.Historical;
					if (cell.visited.knowledge == Knowledge.Historical) cell.visited = new HistoricalValue<bool> { knowledge = Knowledge.Unknown, value = false };
					else if (cell.visited.knowledge != Knowledge.Unknown) cell.visited.knowledge = Knowledge.Historical;
					cells[i] = cell;
				}
			}

			if (version >= 4) {
				levelVoteKeep = new List<int>();
				int cnt = reader.ReadInt32();
				for (int i = 0; i < cnt; i++) levelVoteKeep.Add(reader.ReadInt32());
			}
			Trim();
		}

		public Cell Get(int x, int y) {
			//if (expectedMap != null) {
				/*var expectedCell = expectedMap.Get(x + expectedMapOffset.x, y + expectedMapOffset.y);
				expectedCell.historical = true;
				if (!bounds.Contains(x, y)) return expectedCell;

				var ourCell = GetMut(x, y);
				if (ourCell.state == CellState.Unknown) {
					return expectedCell;
				}

				// The other map might know if there is a ladder in this cell
				ourCell.ladder |= expectedCell.ladder;
				ourCell.visited |= expectedCell.visited;
				return ourCell;*/
			//} else {
				if (!bounds.Contains(x, y)) {
					return new Cell { state = new HistoricalValue<CellState> { knowledge = Knowledge.Unknown, value = CellState.Unknown } };
				} else {
					return GetMut(x, y);
				}
			//}
		}

		public ref Cell GetMut(int x, int y) {
			return ref cells[(y - bounds.ymin)*bounds.Width + (x - bounds.xmin)];
		}

		public void Expand(IntRect newBounds) {
			newBounds = IntRect.Union(bounds, newBounds);
			if (bounds == newBounds) return;

			var relativeBounds = bounds.Offset(new Int2(-newBounds.xmin, -newBounds.ymin));
			Cell[] newCells = new Cell[newBounds.Height*newBounds.Width];
			for (int i = 0; i < newCells.Length; i++) {
				newCells[i] = new Cell { state = new HistoricalValue<CellState> { knowledge = Knowledge.Unknown, value = CellState.Unknown } };
			}

			// Copy old bounds
			for (int y = 0; y < relativeBounds.Height; y++) {
				for (int x = 0; x < relativeBounds.Width; x++) {
					newCells[(y + relativeBounds.ymin)*newBounds.Width + (x + relativeBounds.xmin)] = cells[y*bounds.Width + x];
				}
			}

			cells = newCells;
			bounds = newBounds;
		}

		public float CalculateExplorationScore () {
			float score = 0;
			for (int y = bounds.ymin; y <= bounds.ymax; y++) {
				for (int x = bounds.xmin; x <= bounds.xmax; x++) {
					if (GetMut(x, y).visited.value) score += 1;
					if (GetMut(x, y).ladder.value) score += 1000;
				}
			}
			return score;
		}

		void CopyHistoricalMap() {
			var fullBounds = IntRect.Union(bounds, expectedMap.bounds.Offset(new Int2(-expectedMapOffset.x, -expectedMapOffset.y)));
			Expand(fullBounds);
			if (expectedMap.version >= 4) {
				levelVote.Add(expectedMap.level);
				levelVote.AddRange(expectedMap.levelVoteKeep);
			}
			for (int y = bounds.ymin; y <= bounds.ymax; y++) {
				for (int x = bounds.xmin; x <= bounds.xmax; x++) {
					ref var cell = ref GetMut(x, y);
					var otherCell = expectedMap.Get(x + expectedMapOffset.x, y + expectedMapOffset.y);
					if (cell.state.knowledge != Knowledge.Unknown && cell.state.value == CellState.Unknown) throw new System.Exception("!");
					cell.state.TryApply(otherCell.state);
					cell.visited.TryApply(otherCell.visited);
					cell.ladder.TryApply(otherCell.ladder);
				}
			}
			UpdateVotes();
		}

		public void UpdateVotes () {
			Debug.LogError("Votes: " + string.Join(", ", levelVote.Select(v => v.ToString()).ToArray()));
			var votes = levelVote.Concat(levelVoteKeep).ToList();
			if (votes.Count > 0) {
				float levelSum = 0;
				for (int i = 0; i < votes.Count; i++) levelSum += votes[i];
				level = Mathf.RoundToInt(levelSum / votes.Count);
			}
		}

		int Hash (int x0, int y0, int radius) {
			int patchHash = 0;
			for (int y = y0 - radius; y <= y0 + radius; y++) {
				for (int x = x0 - radius; x <= x0 + radius; x++) {
					var cell = Get(x, y);
					patchHash = patchHash * 31 + Hash(cell);
				}
			}
			return patchHash;
		}

		static int Hash (Cell cell) {
			if (cell.state.knowledge == Knowledge.Unknown || cell.state.value == CellState.Unknown) return 0;
			return (int)cell.state.value + 1;
		}

		void GetMatchingOffsets(int hash, int radius, List<Int2> offsets) {

			/*for (int y0 = bounds.ymin; y0 < bounds.ymax; y0++) {
				for (int x0 = bounds.xmin; x0 < bounds.xmax; x0++) {
					if (Hash(x0, y0, radius) == hash) {
						offsets.Add(new Int2(x0, y0));
					}
				}
			}*/

			int window = radius*2+1;
			if (hashes == null) {
				//List<Int2> refVals = new List<Int2>();
				int[] hashesX = new int[(bounds.Width + window)*(bounds.Height + window)];
				int exp = 1;
				for (int i = 0; i < window; i++) exp *= 31;

				for (int y0 = 0; y0 < bounds.Height + window; y0++) {
					int rollingHash = 0;
					for (int x0 = 0; x0 < bounds.Width + window; x0++) {
						int r0 = rollingHash;
						var cell = Get(x0 + bounds.xmin, y0 + bounds.ymin);
						rollingHash = rollingHash*31 + Hash(cell);

						var cell2 = Get(x0 + bounds.xmin - window, y0 + bounds.ymin);
						rollingHash -= exp*Hash(cell2);

						//int h = 0;
						//for (int x = x0 - window + 1; x <= x0; x++) {
						//	var c = Get(x + bounds.xmin, y0 + bounds.ymin);
						//	h = h*31;
						//	if (c.state.knowledge != Knowledge.Unknown) h += (int)c.state.value;
						//}
						//if (h != rollingHash) throw new System.Exception(h + " " + r0 + " " + rollingHash + " " + x0 + " " + y0 + " " + cell.state.value + " " + cell.state.knowledge);
						hashesX[y0*(bounds.Width + window) + x0] = rollingHash;
					}
				}
					
				int exp2 = 1;
				for (int i = 0; i < window; i++) exp2 *= exp;
				hashes = new int[(bounds.Width + window)*(bounds.Height + window)];
				for (int x0 = 0; x0 < bounds.Width + window; x0++) {
					int rollingHash = 0;
					for (int y0 = 0; y0 < bounds.Height + window; y0++) {
						rollingHash = rollingHash * exp + (int)hashesX[y0*(bounds.Width + window) + x0];

						int v = y0 >= window ? hashesX[(y0 - window)*(bounds.Width + window) + x0] : 0;
						rollingHash -= exp2*v;

						hashes[y0 * (bounds.Width + window) + x0] = rollingHash;
					}
				}
			}

			for (int x0 = 0; x0 < bounds.Width + window; x0++) {
				for (int y0 = 0; y0 < bounds.Height + window; y0++) {
					if (hashes[y0 * (bounds.Width + window) + x0] == hash) {
						offsets.Add(new Int2(x0 - radius + bounds.xmin, y0 - radius + bounds.ymin));
					}
				}
			}
				
			//Debug.Log("Hashing\n" + string.Join(", ", offsets.Select(o => o.ToString())));
			//for (int i = 0; i < offsets.Count; i++) {
			//	if (!refVals.Contains(offsets[i])) {
			//		Debug.LogError("Missing " + offsets[i]);
			//	}
			//}
		}

		HashSet<System.Tuple<Map, Int2>> knownBadOffsets = new HashSet<System.Tuple<Map, Int2>>();
		List<Map> matchedMaps = new List<Map>();

		public IEnumerator MatchLevels(List<Map> historicMaps, Int2 playerPos) {
			for(int j = 0; j < 5; j++) {
				expectedMap = null;
				historicMaps.Sort((a,b) => -a.CalculateExplorationScore().CompareTo(b.CalculateExplorationScore()));
				float bestScore = 0;
				Int2 hashPos = default;
				int hash = 0;
				for (int i = 0; i < 10 && hash == 0; i++) {
					hashPos = new Int2(Random.Range(bounds.xmin + 4, bounds.xmax - 4), Random.Range(bounds.ymin + 4, bounds.ymax - 4)); //playerPos + new Int2(Random.Range(-10, 10), Random.Range(-10, 10));
					hash = Hash(hashPos.x, hashPos.y, 4);
				}

				// Give up
				if (hash == 0) yield break;

				foreach (var other in historicMaps) {
					if (matchedMaps.Contains(other)) continue;
					Profiler.BeginSample("Find hashing offsets");
					List<Int2> validOffsets = new List<Int2>();
					var explorationScore = other.CalculateExplorationScore();
					other.GetMatchingOffsets(hash, 4, validOffsets);
					Profiler.EndSample();

					Profiler.BeginSample("Validate offsets");
					for (int i = 0; i < validOffsets.Count; i++) {
						var offset = validOffsets[i] - hashPos;
						if (other.Hash(hashPos.x + offset.x, hashPos.y + offset.y, 4) != hash) throw new System.Exception("!");
						//if (knownBadOffsets.Contains(new System.Tuple<Map, Int2> (other, offset))) continue;
						//Debug.Log("Testing " + offset);
						bool match = true;
						int matching = 0;
						var relativeOverlap = IntRect.Intersection(bounds.Offset(offset), other.bounds).Offset(new Int2(-offset.x, -offset.y));
						for (int y = relativeOverlap.ymin; y <= relativeOverlap.ymax && match; y++) {
							for (int x = relativeOverlap.xmin; x <= relativeOverlap.xmax && match; x++) {
								var s1 = GetMut(x, y);
								var s2 = other.GetMut(x + offset.x, y + offset.y);
								if (s1.state.knowledge != Knowledge.Unknown && s2.state.knowledge != Knowledge.Unknown && s1.state.value != CellState.Unknown && s2.state.value != CellState.Unknown) {
									if (s1.state.value != s2.state.value) {
										match = false;
									} else {
										matching++;
									}
								}
							}
						}

						//Debug.Log(match + " " + matching);
						if (match && matching > 100) {
							float score = matching + explorationScore*0.1f;
							if (score > bestScore) {
								bestScore = score;
								expectedMap = other;
								expectedMapOffset = offset;
							}
						}
					}
					Profiler.EndSample();
					/*for (int y0 = other.bounds.ymin - bounds.Height; y0 <= other.bounds.ymax; y0++) {
						for (int x0 = other.bounds.xmin - bounds.Width; x0 <= other.bounds.xmax; x0++) {
							bool match = true;
							int matching = 0;
							var offset = new Int2(x0 - bounds.xmin + other.bounds.xmin, y0 - bounds.ymin + other.bounds.ymin);
							var relativeOverlap = IntRect.Intersection(bounds.Offset(offset), other.bounds).Offset(new Int2(-offset.x, -offset.y));
							for (int y = relativeOverlap.ymin; y <= relativeOverlap.ymax && match; y++) {
								for (int x = relativeOverlap.xmin; x <= relativeOverlap.xmax && match; x++) {
									var s1 = GetMut(x, y);
									var s2 = other.GetMut(x + offset.x, y + offset.y);
									if (s1.state.knowledge != Knowledge.Unknown && s2.state.knowledge != Knowledge.Unknown && s1.state.value != CellState.Unknown && s2.state.value != CellState.Unknown) {
										if (s1.state.value != s2.state.value) {
											match = false;
										} else {
											matching++;
										}
									}
								}
							}

							if (match && matching > 100) {
								float score = matching + explorationScore*0.1f;
								if (score > bestScore) {
									bestScore = score;
									expectedMap = other;
									expectedMapOffset = offset;
								}
							}
						}
					}*/
					yield return null;
				}

				if (expectedMap != null) {
					Debug.LogError("Applying historical map " + expectedMap.bounds + " " + expectedMapOffset + " " + bestScore + " " + expectedMap.version);
					matchedMaps.Add(expectedMap);
					CopyHistoricalMap();
				}
			}
			yield break;
		}

		public void UpdateTilemap (Tilemap tilemap, TileBase[] tiles, PrefabTilemap prefabTilemap, Int2 offset = default) {
			var fullBounds = bounds;
			if (expectedMap != null) {
				fullBounds = IntRect.Union(fullBounds, expectedMap.bounds.Offset(new Int2(-expectedMapOffset.x, -expectedMapOffset.y)));
			}

			for (int y = fullBounds.ymin; y <= fullBounds.ymax; y++) {
				for (int x = fullBounds.xmin; x <= fullBounds.xmax; x++) {
					var state = GetState(x, y);
					var tile = tiles[(int)state];
					if (Get(x, y).ladder.value) tile = tiles[4];
					tilemap.SetTile(new Vector3Int(x + offset.x, y + offset.y, 0), tile);
					if (prefabTilemap != null) prefabTilemap.Set(new Int2(x + offset.x, y + offset.y), state == CellState.Occupied);
				}
			}
		}
	}

	public class GameState {
		public Int2 playerPos = new Int2(0, 0);
		public int currentMap = 0;

		public List<Map> maps = new List<Map> { new Map { level = 0 } };
		public Game game;
		public int frame;
		const int ViewMargin = 10;
		List<Entity> entities = new List<Entity>();
		List<string> events = new List<string>();
		public List<Item> items = new List<Item>();
		IEnumerator matcherCoroutine;
		public Entity player;

		public bool HasAnyInfoAbout(Int2 p) {
			return maps[currentMap].HasAnyInfoAbout(p);
		}

		public CellState GetState(Int2 p) {
			return maps[currentMap].GetState(p.x, p.y);
		}

		public Cell Get(Int2 p) {
			return maps[currentMap].Get(p.x, p.y);
		}

		public ref Cell GetMut(Int2 p) {
			return ref maps[currentMap].GetMut(p.x, p.y);
		}

		public CellState GetState(int x, int y) {
			return maps[currentMap].GetState(x, y);
		}

		public ref Cell GetMut(int x, int y) {
			return ref maps[currentMap].GetMut(x, y);
		}

			
		void ShowEvents(List<string> events, int count) {
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			foreach (var ev in events.AsEnumerable().Reverse().Where(e => e != "you moved").Take(count).Reverse()) {
				sb.AppendLine(ev);
			}
			game.eventLog.text = sb.ToString();
		}

		void UpdateTilemap () {
			maps[currentMap].UpdateTilemap(game.tilemap, game.tiles, game.prefabTilemap);
		}

		void UpdateVisual () {
			ShowEvents(events, 8);
			UpdateTilemap();
			game.levelText.text = "Level " + (maps[currentMap].level + 1);
		}

		List<Map> historicalMaps = new List<Map>();
		public void LoadMaps() {
			matcherCoroutine = null;
			game.tilemap2.ClearAllTiles();
			historicalMaps.Clear();
			int offset = 0;
			foreach (var path in System.IO.Directory.GetFiles("maps")) {
				var map = new Map();
				map.Deserialize(System.IO.File.ReadAllBytes(path));
				historicalMaps.Add(map);
				map.UpdateTilemap(game.tilemap2, game.tiles, null, new Int2(offset - map.bounds.xmin, -map.bounds.ymin));
				offset += map.bounds.Width + 1;
			}

			// Hack
			//maps[0].Deserialize(System.IO.File.ReadAllBytes("maps/0_1679503104.binary"));
			//UpdateTilemap();
		}

		void SaveMap () {
			var map = maps[currentMap];
			System.IO.Directory.CreateDirectory("maps");
			System.IO.File.WriteAllBytes("maps/" + currentMap + "_" + Mathf.Abs((int)map.GetHashCode()) + ".binary", map.Serialize());
		}

		public void Apply(Observation obs) {
			frame++;
			var observationBounds = new IntRect(playerPos.x - obs.radius, playerPos.y - obs.radius, playerPos.x + obs.radius, playerPos.y + obs.radius);

			int itemsHad = items.Count;
			Match(
				items,
				obs.itemNames.ToList(),
				matchCost: (item, obsItem) => {
					if (item.name == obsItem) return 1;
					return null;
				},
				onCreate: obsItem => {
					var item = new Item { name = obsItem };
					item.Spawn(game);
					return item;
				},
				onUpdate: (item, obsItem) => {
					// Items don't change
				},
				onMissing: item => {
					item.OnLost();
					return MissingAction.Remove;
				}
			);

			if ((items.Count == 0 && itemsHad > 0) || obs.HasEvent(playerName + " was born")) {
				Debug.Log("Lost all items, probably died");
				foreach (var e in entities) {
					if (e != player) e.OnDeath(game);
				}
				entities.RemoveAll(e => e != player);
				currentMap = 0;
				maps = new List<Map> { new Map { level = 0, levelVoteKeep = { 0, 0, 0, 0, 0, 0, 0 } } };
				LoadMaps();
				game.PlaySoundEffect(Sfx.PlayerDied);
			}

			Match(
				entities,
				obs.players.Concat(obs.monsters).ToList(),
				matchCost: (gameEntity, obsEntity) => {
					if (gameEntity.name == obsEntity.name && gameEntity.type == obsEntity.type) {
						var dist = (gameEntity.pos - (playerPos + new Int2(obsEntity.relativePos.col, obsEntity.relativePos.row))).sqrMagnitudeLong;
						if (dist < 2*2) {
							return (float)dist;
						}
					}
					return null;
				},
				onCreate: obsEntity => {
					var pos = playerPos + new Int2(obsEntity.relativePos.col, obsEntity.relativePos.row);
					var gameEntity = new Entity(pos, obsEntity.name, obsEntity.type, frame);
					gameEntity.Spawn(game);
					return gameEntity;
				},
				onUpdate: (gameEntity, obsEntity) => {
					gameEntity.pos = playerPos + new Int2(obsEntity.relativePos.col, obsEntity.relativePos.row);
					gameEntity.health = obsEntity.health;
				},
				onMissing: gameEntity => {
					// Dead?
					gameEntity.OnDeath(game);
					return MissingAction.Remove;
				}
			);

			if (obs.HasEvent("you fell down a ladder")) {
				Debug.LogError("Fell down a ladder");
				GetMut(playerPos).ladder = new HistoricalValue<bool> { knowledge = Knowledge.Known, value = true };
				SaveMap();
				game.tilemap.ClearAllTiles();
				var newMap = new Map { level = maps[currentMap].level + 1 };
				newMap.levelVoteKeep.AddRange(maps[currentMap].levelVote.Concat(maps[currentMap].levelVoteKeep).Select(v => v + 1));
				currentMap++;
				newMap.UpdateVotes();
				maps.Add(newMap);
				game.PlaySoundEffect(Sfx.FellDownLadder);
			}

			Match<Item, string>(
				items,
				obs.equipped.ToList(),
				matchCost: (item, obsItem) => {
					if (item.name == obsItem) return item.equipped ? 0 : 1;
					return null;
				},
				onCreate: obsItem => {
					// Shouldn't happen really
					throw new System.Exception();
				},
				onUpdate: (item, obsItem) => {
					if (!item.equipped) item.OnEquipped();
				},
				onMissing: item => {
					item.OnUnequipped();
					return MissingAction.Keep;
				}
			);

			maps[currentMap].Expand(observationBounds.Expand(ViewMargin));

			foreach (var e in obs.events) {
				if (e.Contains(playerName + " dealt")) {
					var weapon = items.FirstOrDefault(item => item.equipped && HalloweenBot.Classify(item) == HalloweenBot.EquipmentTag.Weapon);
					if (weapon == null) {
						game.PlaySoundEffect(Sfx.AttackHands);
					}  else if (weapon.name.ToLowerInvariant() == "it isn't") {
						game.PlaySoundEffect(Sfx.AttackMagic);
					} else {
						game.PlaySoundEffect(Sfx.AttackSword);
					}
				}
				events.Add(e);
			}
				
			for (int y = 0; y < observationBounds.Height; y++) {
				for (int x = 0; x < observationBounds.Width; x++) {
					var p = new Int2(x + observationBounds.xmin, y + observationBounds.ymin);
					maps[currentMap].units.Remove(p);
					ref var cell = ref GetMut(p.x, p.y);
					if (obs.surrounding[y, x] != cell.state.value && cell.state.value != CellState.Unknown) {
						if (cell.state.knowledge == Knowledge.Historical) {
							// Actual data differs from historical. Historical data probably not accurate. Clear it to regenerate
							Debug.Log("Historical data is inaccurate");
						} else {
							Debug.Log("Map data is inaccurate");
						}
						maps[currentMap].ClearHistorical();
						game.tilemap.ClearAllTiles();
					}
					cell.state = new HistoricalValue<CellState> { knowledge = Knowledge.Known, value = obs.surrounding[y, x] };
				}
			}

			List<Int2> toRemove = new List<Int2>();
			foreach (var kv in maps[currentMap].units) {
				if (observationBounds.Contains(kv.Value.entity.pos.x, kv.Value.entity.pos.y)) {
					toRemove.Add(kv.Key);
				}
			}
			foreach (var p in toRemove) maps[currentMap].units.Remove(p);

			for (int i = 0; i < entities.Count; i++) {
				maps[currentMap].units.Add(entities[i].pos, new SeenEntity { entity = entities[i], frame = frame });
			}

			GetMut(playerPos.x, playerPos.y).visited = new HistoricalValue<bool> { knowledge = Knowledge.Known, value = true };

			if (GetMut(playerPos.x, playerPos.y).ladder.knowledge == Knowledge.Historical && GetMut(playerPos.x, playerPos.y).ladder.value) {
				Debug.LogError("Historical ladder data is inaccurate");
				maps[currentMap].ClearHistorical();
				game.tilemap.ClearAllTiles();
			}
			GetMut(playerPos.x, playerPos.y).ladder = new HistoricalValue<bool> { knowledge = Knowledge.Known, value = false };

			if (matcherCoroutine == null) {
				matcherCoroutine = maps[currentMap].MatchLevels(historicalMaps, playerPos);
			}

			if (maps[currentMap].units.ContainsKey(playerPos)) {
				player = maps[currentMap].units[playerPos].entity;
			} else {
				player = null;
			}

			//game.playerSprite.position = new Vector2(playerPos.x, playerPos.y);
			UpdateVisual();
			SaveMap();
		}

		public void Update () {
			if (matcherCoroutine != null && !matcherCoroutine.MoveNext()) matcherCoroutine = null;
			foreach (var entity in entities) entity.Update();
		}
	}

	GameState game = new GameState();
	HalloweenBot bot;

	float lastMoveTime = 0;
	float lastLookTime = 0;
	HalloweenBot.Status lastStatus;
	List<HalloweenBot.Status> recentStatus = new List<HalloweenBot.Status>();
	float lastChangeStatus = -1;

    // Start is called before the first frame update
    void Start() {
		game.game = this;
		bot = new HalloweenBot(game);
		bot.behavior = HalloweenBot.Behavior.AutomatedExploration;
		game.LoadMaps();
		StartCoroutine(Look());
		statusLabel.text = "";
    }

	void Update() {
		if (currentRequest.request != null && currentRequest.request.isDone) {
			var tmp = currentRequest;
			currentRequest = default;
			ParseObservation(tmp.request.downloadHandler.text, tmp.done);
		}

		if (currentRequest.request == null && requestQueue.Count > 0) {
			currentRequest = requestQueue.Dequeue();
			currentRequest.request.SendWebRequest();
		}

		if (canMove) {
			if (Input.GetKey(KeyCode.LeftArrow)) {
				StartCoroutine(Move(Direction.West));
			}
			if (Input.GetKey(KeyCode.RightArrow)) {
				StartCoroutine(Move(Direction.East));
			}
			if (Input.GetKey(KeyCode.UpArrow)) {
				StartCoroutine(Move(Direction.South));
			}
			if (Input.GetKey(KeyCode.DownArrow)) {
				StartCoroutine(Move(Direction.North));
			}
		}

		if (Time.realtimeSinceStartup - lastMoveTime > 0.6f && Time.realtimeSinceStartup - lastLookTime > 0.2f && currentRequest.request == null) {
			StartCoroutine(Look());
		}

		bot.Update();
		game.Update();

		globalLight.enabled = true;
		minimapCamera.Render();
		globalLight.enabled = false;

		if (lastStatus != bot.status || Time.time - lastChangeStatus > 40) {
			lastStatus = bot.status;
			if (!recentStatus.Contains(bot.status)) {
				var alts = statusTexts[bot.status];
				statusLabel.text = alts[Random.Range(0, alts.Count - 1)];
				lastChangeStatus = Time.time;
				recentStatus.Add(bot.status);
			}
		}

		if (Time.time - lastChangeStatus > 10 && statusLabel.text != "") {
			statusLabel.text = "";
			lastChangeStatus = Time.time;
		}

		if (Time.time - lastChangeStatus > 5 && recentStatus.Count > 0) {
			recentStatus.RemoveAt(0);
		}

		if (game.player != null) {
			statusLabel.transform.parent.position = cam.WorldToScreenPoint(game.player.interpolatedPos);
		}
	}

	struct Request {
		public UnityWebRequest request;
		public System.Action<Observation> done;
	}

	Request currentRequest;
	Queue<Request> requestQueue = new Queue<Request>();

	IEnumerator Get(string endpoint, Dictionary<string, string> parameters = null, System.Action<Observation> done = null) {
		var paramString = "";
		if (parameters != null) {
			foreach (var kv in parameters) {
				paramString += "&" + kv.Key + "=" + kv.Value;
			}
		}

		var request = new Request {
			request = UnityWebRequest.Get("http://halloween.kodsport.se/" + endpoint + "?apiKey=" + apiKey + paramString),
			done = done
		};

		requestQueue.Enqueue(request);
		yield return request.request;
	}

	public bool isBusy => currentRequest.request != null;
	public bool canMove => !isBusy && Time.realtimeSinceStartup - lastMoveTime > 0.49f;

	public void MoveTowards(Int2 p) {
		var rel = p - game.playerPos;
		Direction direction = Direction.North;
		if (Mathf.Abs(rel.x) > Mathf.Abs(rel.y)) {
			direction = rel.x > 0 ? Direction.East : Direction.West;
		} else {
			direction = rel.y > 0 ? Direction.South : Direction.North;
		}
		StartCoroutine(Move(direction));
	}

	IEnumerator Move(Direction direction) {
		yield return Get("move", new Dictionary<string, string> { { "moveDirection", direction.ToString() } }, obs => {
			lastMoveTime = Time.realtimeSinceStartup;
			if (obs.HasEvent("you moved") || obs.HasEvent("you fell down a ladder")) {
				game.playerPos += Direction2delta[(int)direction];
				PlaySoundEffect(Sfx.Step);
			}
		});
	}
		
	string[] expectedKeys = {
		"itemNames",
		"surrounding",
		"radius",
		"players",
		"events",
		"monsters",
		"equipped",
		"row",
		"col",
		"name",
		"relativePos",
		"health",
		"error",
	};
		
	void ParseObservation(string responseText, System.Action<Observation> ok) {
		foreach (Match key in Regex.Matches(responseText, @"""(\w+)\"":")) {
			if (!expectedKeys.Contains(key.Groups[1].Value)) Debug.LogError("Unexpected key " + key);
		}

		Debug.Log("Response\n" + responseText);
		var rawObservation = TinyJsonDeserializer.Deserialize(responseText, typeof(RawObservation)) as RawObservation;
		if (rawObservation.error != null) {
			Debug.LogError(rawObservation.error);
			return;
		}
		var observation = new Observation(rawObservation);
		ok?.Invoke(observation);
		game.Apply(observation);
	}

	IEnumerator Look () {
		lastLookTime = Time.realtimeSinceStartup;
		yield return Get("look");
	}

	void Use (Item item, System.Action ok) {
		StartCoroutine(Get("use", new Dictionary<string, string> { { "itemName", item.name } }, obs => {
			if (obs.HasEvent(playerName + " used " + item.name)) {
				ok?.Invoke();
			}
		}));
	}

	public void PlaySoundEffect(Sfx type) {
		StartCoroutine(soundEffects.FirstOrDefault(s => s.type == type)?.Play());
	}
}
