using UnityEngine;
using System.Collections.Generic;
using Pathfinding;
using System.Linq;

public class HalloweenBot {
	Game.GameState state;
	public Behavior behavior = Behavior.None;
	public Status status;

	public HalloweenBot (Game.GameState state) {
		this.state = state;
	}

	public enum Behavior {
		None,
		AutomatedExploration,
	}

	public enum EquipmentTag {
		Unknown,
		Weapon,
		Potion,
		Breastplate,
		Helmet,
		Legplates,
	}

	string[] equipmentOrder = {
		"it isn't",
		"potion of fortitude",
		"potion of strength", // +33% damage (10 seconds?)
		"small health potion",
		"dagger of vindication",
		"skull breastplate",
		"skull helmet",
		"skull legplates",
		"bronze breastplate",
		"bronze helmet",
		"bronze legplates",
		"iron breastplate",
		"iron helmet",
		"iron legplates",
		"steel breastplate",
		"steel helmet",
		"steel legplates",
		"gold breastplate",
		"gold helmet",
		"gold legplates",
		"diamond breastplate",
		"diamon helmet",
		"diamond legplates",
		"broadsword of dooooooom",
		"blessed axe",
		"kingslayer",
		"bloodcursed slicer",
	};

	public enum Status {
		LookingForLadder,
		FleeingFromIT,
		Fleeing,
		LookingForMonsters,
		MovingToLadder,
		AttackingMonster,
		Idle,
	}

	public static EquipmentTag Classify(Game.Item item) {
		var name = item.name.ToLowerInvariant();
		if (name.Contains("potion")) return EquipmentTag.Potion;

		if (name.Contains("sword")) return EquipmentTag.Weapon;
		if (name.Contains("dagger")) return EquipmentTag.Weapon;
		if (name.Contains("axe")) return EquipmentTag.Weapon;
		if (name.Contains("spear")) return EquipmentTag.Weapon;
		if (name.Contains("kingslayer")) return EquipmentTag.Weapon;
		if (name.Contains("bloodcursed slicer")) return EquipmentTag.Weapon;

		if (name.Contains("it isn't")) return EquipmentTag.Weapon;

		if (name.Contains("breastplate")) return EquipmentTag.Breastplate;
		if (name.Contains("helmet")) return EquipmentTag.Helmet;
		if (name.Contains("legplates")) return EquipmentTag.Legplates;
		return EquipmentTag.Unknown;
	}

	int EquipmentScore(string name) {
		var idx = System.Array.IndexOf(equipmentOrder, name);

		if (name == "it isn't" && IsNearby("IT", 1) != null) return 100;

		// Haven't seen it before: it's probably good
		if (idx == -1) return 1000;
		return idx;
	}

	void OptimizeEquipment() {
		foreach (var group in state.items.GroupBy(Classify)) {
			if (group.Key != EquipmentTag.Potion && group.Key != EquipmentTag.Unknown) {
				var ordered = group.OrderByDescending(item => EquipmentScore(item.name.ToLowerInvariant()));
				var best = ordered.FirstOrDefault();
				if (best != null) {
					if (!best.equipped) best.Use();
				}
			}
		}
	}

	Game.Entity IsNearby(string entityName, int radius) {
		for (int dx = -radius; dx <= radius; dx++) {
			for (int dy = -radius; dy <= radius; dy++) {
				Game.SeenEntity unitAtTile;
				if (state.maps[state.currentMap].units.TryGetValue(state.playerPos + new Int2(dx, dy), out unitAtTile)) {
					if (unitAtTile.frame > state.frame - 5 && unitAtTile.entity != state.player && (entityName == null || unitAtTile.entity.name == entityName)) {
						return unitAtTile.entity;
					}
				}
			}
		}
		return null;
	}

	Game.Entity EntityAt(Int2 p) {
		Game.SeenEntity unitAtTile;
		if (state.maps[state.currentMap].units.TryGetValue(p, out unitAtTile)) {
			if (unitAtTile.frame > state.frame - 5 && unitAtTile.entity != state.player) {
				return unitAtTile.entity;
			}
		}
		return null;
	}

	float PotionScore (string name) {
		name = name.ToLowerInvariant();
		if (name.Contains("large")) return 3;
		if (name.Contains("bib")) return 3;
		if (name.Contains("medium")) return 2;
		if (name.Contains("small")) return 1;
		return 0;
	}

	float EnemyHealthAround(Int2 p) {
		float sum = 0;
		int radius = 1;
		for (int i = 0; i < Game.Direction2delta.Length; i++) {
			if (state.maps[state.currentMap].units.TryGetValue(p + Game.Direction2delta[i], out Game.SeenEntity unitAtTile)) {
				if (unitAtTile.frame > state.frame - 5 && unitAtTile.entity != state.player) {
					sum += unitAtTile.entity.health;
				}
			}
		}
			
		if (state.maps[state.currentMap].units.TryGetValue(p, out Game.SeenEntity unitAtCenterTile)) {
			if (unitAtCenterTile.frame > state.frame - 5 && unitAtCenterTile.entity != state.player) {
				sum += unitAtCenterTile.entity.health * 3;
			}
		}
		return sum;
	}

	bool HasItem(string name) {
		name = name.ToLowerInvariant();
		return state.items.Any(item => item.name.ToLowerInvariant() == name);
	}

	const float InfCost = 10000;
	float TargetCost(Int2 p) {
		var entity = EntityAt(p);
		if (entity == null) return 0;

		if (entity.name == "IT") return HasItem("it isn't") ? 0 : InfCost;

		if (entity.health > state.player.health * 2) return InfCost;

		return entity.health;
	}

	float TargetCostDesperate(Int2 p) {
		var entity = EntityAt(p);
		if (entity == null) return 0;

		if (entity.name == "IT") return HasItem("it isn't") ? 0 : 10000;

		return entity.health;
	}

	float maxHealth = 0;
	public void Update () {
		if (!state.game.canMove || state.player == null) return;

		OptimizeEquipment();

		if (behavior == Behavior.AutomatedExploration) {

		}

		// Flee from IT if we don't have the weapon
		if (IsNearby("IT", 1) != null && !HasItem("it isn't")) {
			var fleePoint = Dijkstra(4, p => EnemyHealthAround(p));
			if (fleePoint != null) {
				status = Status.FleeingFromIT;
				state.game.MoveTowards(fleePoint.Value);
				return;
			}
		}

		if (state.player.health < state.player.maxSeenHealth * 0.5f && IsNearby(null, 1) != null) {
			// Use potion if we are low on health
			var potion = state.items.Where(item => Classify(item) == EquipmentTag.Potion).OrderByDescending(item => PotionScore(item.name)).FirstOrDefault();
			if (potion != null) {
				potion?.Use();
			} else {
				status = Status.Fleeing;
				// Flee if we don't have a potion
				var fleePoint = Dijkstra(4, p => EnemyHealthAround(p));
				if (fleePoint != null) {
					state.game.MoveTowards(fleePoint.Value);
					return;
				}
			}
		}
		//if (state.player.health < state.player) {
		//}
		//lastHealth = state.player.health;

		var level = state.maps[state.currentMap].level + 1;
		bool allowFindLadder = true;
		if (level >= 3 && !HasItem("it isn't")) allowFindLadder = false;
		if (level >= 2 && !HasItem("broadsword of dooooooom") && !HasItem("dagger of vindication")) allowFindLadder = false;
		if (level >= 3 && !HasItem("blessed axe")) allowFindLadder = false;
		if (level >= 5 && !HasItem("kingslayer")) allowFindLadder = false;
		if (level >= 3 && (!HasItem("skull helmet") || !HasItem("skull breastplate") || !HasItem("skull legplates"))) allowFindLadder = false;
		if (level >= 4 && (!HasItem("bronze helmet") || !HasItem("bronze breastplate") || !HasItem("bronze legplates"))) allowFindLadder = false;
		if (level >= 6 && (!HasItem("iron helmet") || !HasItem("iron breastplate") || !HasItem("iron legplates"))) allowFindLadder = false;
		if (level >= 9 && (!HasItem("steel helmet") || !HasItem("steel breastplate") || !HasItem("steel legplates"))) allowFindLadder = false;

		var enemySearchRange = allowFindLadder ? 4 : 8;
		var point = Dijkstra(enemySearchRange, p => TargetCost(p) + 1, p => EntityAt(p) != null && TargetCost(p) < InfCost);
		if (point != null) {
			status = Status.AttackingMonster;
			state.game.MoveTowards(point.Value);
			return;
		}
			
		var ladderPoint = BFS(p => state.Get(p).ladder.value);
		if (allowFindLadder) {
			if (ladderPoint != null) {
				status = Status.MovingToLadder;
				state.game.MoveTowards(ladderPoint.Value);
				return;
			}
		}

		// If we have not found a ladder yet, then search for it
		if (ladderPoint == null) {
			point = BFS(p => !state.Get(p).visited.value && state.Get(p).state.value != Game.CellState.Occupied);
			if (point != null) {
				status = Status.LookingForLadder;
				state.game.MoveTowards(point.Value);
				return;
			}

			point = BFS(p => state.Get(p).visited.knowledge != Game.Knowledge.Known && state.Get(p).state.value != Game.CellState.Occupied);
			if (point != null) {
				status = Status.LookingForLadder;
				state.game.MoveTowards(point.Value);
				return;
			}
		}

		point = BFS(p => state.Get(p).state.knowledge != Game.Knowledge.Known && state.Get(p).state.value != Game.CellState.Occupied);
		if (point != null) {
			status = Status.LookingForMonsters;
			state.game.MoveTowards(point.Value);
			return;
		}

		// If we cannot walk anywhere then we might be stuck. Attack the enemy with the lowest health
		point = Dijkstra(1, p => TargetCostDesperate(p) + 1, p => EntityAt(p) != null && TargetCostDesperate(p) < InfCost);
		if (point != null) {
			status = Status.AttackingMonster;
			state.game.MoveTowards(point.Value);
			return;
		}

		status = Status.Idle;
	}

	Int2? BFS(System.Func<Int2, bool> validEndpoint, int maxDistance = int.MaxValue) {
		Queue<Int2> queue = new Queue<Int2>();
		Dictionary<Int2, Int2> parents = new Dictionary<Int2, Int2>();
		Dictionary<Int2, int> depth = new Dictionary<Int2, int>();
		Int2 startPoint = state.playerPos;
		queue.Enqueue(startPoint);
		depth[startPoint] = 0;
		while(queue.Count > 0) {
			var p = queue.Dequeue();
			var ourDepth = depth[p];
			if (state.GetState(p) == Game.CellState.Occupied) {
				continue;
			}

			Game.SeenEntity unitAtTile;
			if (state.maps[state.currentMap].units.TryGetValue(p, out unitAtTile)) {
				if (unitAtTile.frame > state.frame - 100 && unitAtTile.entity.health > state.player.health) {
					continue;
				}
			}

			if (validEndpoint(p)) {
				List<Int2> path = new List<Int2>();
				while(p != startPoint) {
					path.Add(p);
					Debug.DrawLine(new Vector2(p.x, p.y), new Vector2(parents[p].x, parents[p].y), Color.red, 0.5f);
					p = parents[p];
				}
				if (path.Count > 0) return path[path.Count-1];
				break;
			}

			if (!state.HasAnyInfoAbout(p)) {
				continue;
			}

			if (ourDepth >= maxDistance) continue;

			for (int i = 0; i < 4; i++) {
				var other = p + Game.Direction2delta[i];
				if (!parents.ContainsKey(other)) {
					parents[other] = p;
					depth[other] = ourDepth + 1;
					queue.Enqueue(other);
				}
			}
		}
		return null;
	}

	Int2? Dijkstra(int maxDistance, System.Func<Int2, float> cost, System.Func<Int2, bool> validEndpoint = null) {
		List<Int2> queue = new List<Int2>();
		List<float> queueCost = new List<float>();

		Dictionary<Int2, Int2> parents = new Dictionary<Int2, Int2>();
		Dictionary<Int2, int> depth = new Dictionary<Int2, int>();
		Int2 startPoint = state.playerPos;
		queue.Add(startPoint);
		queueCost.Add(0);
		depth[startPoint] = 0;
		while(queue.Count > 0) {
			int minIndex = 0;
			for (int i = 0; i < queue.Count; i++) {
				if (queueCost[i] < queueCost[minIndex]) {
					minIndex = i;
				}
			}
			var p = queue[minIndex];
			var ourCost = queueCost[minIndex];
			queue.RemoveAt(minIndex);
			queueCost.RemoveAt(minIndex);
			var ourDepth = depth[p];

			if (state.GetState(p) == Game.CellState.Occupied) {
				continue;
			}

			Game.SeenEntity unitAtTile;
			if (state.maps[state.currentMap].units.TryGetValue(p, out unitAtTile)) {
				if (unitAtTile.frame > state.frame - 100 && unitAtTile.entity.health > state.player.health) {
					continue;
				}
			}

			if (validEndpoint == null ? ourDepth >= maxDistance : validEndpoint(p)) {
				List<Int2> path = new List<Int2>();
				while(p != startPoint) {
					path.Add(p);
					Debug.DrawLine(new Vector2(p.x, p.y), new Vector2(parents[p].x, parents[p].y), Color.red, 0.5f);
					p = parents[p];
				}
				if (path.Count > 0) return path[path.Count-1];
				break;
			}

			if (ourDepth >= maxDistance || !state.HasAnyInfoAbout(p)) {
				continue;
			}

			for (int i = 0; i < 4; i++) {
				var other = p + Game.Direction2delta[i];
				if (!parents.ContainsKey(other)) {
					parents[other] = p;
					depth[other] = ourDepth + 1;
					queue.Add(other);
					queueCost.Add(ourCost + cost(other));
				}
			}
		}
		return null;
	}
}
