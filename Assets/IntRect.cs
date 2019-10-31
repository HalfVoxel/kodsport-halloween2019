using Pathfinding;

/** Integer Rectangle.
 * Works almost like UnityEngine.Rect but with integer coordinates
 */
[System.Serializable]
public struct IntRect {
	public int xmin, ymin, xmax, ymax;

	public IntRect (int xmin, int ymin, int xmax, int ymax) {
		this.xmin = xmin;
		this.xmax = xmax;
		this.ymin = ymin;
		this.ymax = ymax;
	}

	public bool Contains (int x, int y) {
		return !(x < xmin || y < ymin || x > xmax || y > ymax);
	}

	public bool Contains (IntRect other) {
		return xmin <= other.xmin && xmax >= other.xmax && ymin <= other.ymin && ymax >= other.ymax;
	}

	public int Width {
		get {
			return xmax-xmin+1;
		}
	}

	public int Height {
		get {
			return ymax-ymin+1;
		}
	}

	/** Returns if this rectangle is valid.
	 * An invalid rect could have e.g xmin > xmax.
	 * Rectamgles with a zero area area invalid.
	 */
	public bool IsValid () {
		return xmin <= xmax && ymin <= ymax;
	}

	public static bool operator == (IntRect a, IntRect b) {
		return a.xmin == b.xmin && a.xmax == b.xmax && a.ymin == b.ymin && a.ymax == b.ymax;
	}

	public static bool operator != (IntRect a, IntRect b) {
		return a.xmin != b.xmin || a.xmax != b.xmax || a.ymin != b.ymin || a.ymax != b.ymax;
	}

	public override bool Equals (System.Object obj) {
		var rect = (IntRect)obj;

		return xmin == rect.xmin && xmax == rect.xmax && ymin == rect.ymin && ymax == rect.ymax;
	}

	public override int GetHashCode () {
		return xmin*131071 ^ xmax*3571 ^ ymin*3109 ^ ymax*7;
	}

	/** Returns the intersection rect between the two rects.
	 * The intersection rect is the area which is inside both rects.
	 * If the rects do not have an intersection, an invalid rect is returned.
	 * \see IsValid
	 */
	public static IntRect Intersection (IntRect a, IntRect b) {
		return new IntRect(
			System.Math.Max(a.xmin, b.xmin),
			System.Math.Max(a.ymin, b.ymin),
			System.Math.Min(a.xmax, b.xmax),
			System.Math.Min(a.ymax, b.ymax)
			);
	}

	/** Returns if the two rectangles intersect each other
	 */
	public static bool Intersects (IntRect a, IntRect b) {
		return !(a.xmin > b.xmax || a.ymin > b.ymax || a.xmax < b.xmin || a.ymax < b.ymin);
	}

	/** Returns a new rect which contains both input rects.
	 * This rectangle may contain areas outside both input rects as well in some cases.
	 */
	public static IntRect Union (IntRect a, IntRect b) {
		return new IntRect(
			System.Math.Min(a.xmin, b.xmin),
			System.Math.Min(a.ymin, b.ymin),
			System.Math.Max(a.xmax, b.xmax),
			System.Math.Max(a.ymax, b.ymax)
			);
	}

	/** Returns a new IntRect which is expanded to contain the point */
	public IntRect ExpandToContain (int x, int y) {
		return new IntRect(
			System.Math.Min(xmin, x),
			System.Math.Min(ymin, y),
			System.Math.Max(xmax, x),
			System.Math.Max(ymax, y)
			);
	}

	/** Returns a new IntRect which has been moved by an offset */
	public IntRect Offset (Int2 offset) {
		return new IntRect(xmin + offset.x, ymin + offset.y, xmax + offset.x, ymax + offset.y);
	}

	/** Returns a new rect which is expanded by \a range in all directions.
	 * \param range How far to expand. Negative values are permitted.
	 */
	public IntRect Expand (int range) {
		return new IntRect(xmin-range,
			ymin-range,
			xmax+range,
			ymax+range
			);
	}

	public override string ToString () {
		return "[x: "+xmin+"..."+xmax+", y: " + ymin +"..."+ymax+"]";
	}
}