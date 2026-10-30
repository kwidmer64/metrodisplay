// Mirrors the parts of MetroDisplay.Contracts the renderer reads. Written by hand for now;
// slice 15 replaces this file with types generated from the C# records.

export interface NetworkScene {
  artifactVersion: string;
  extent: ExtentInfo;
  lines: LineScene[];
  stations: StationMarker[];
  water: WaterArea[];
}

export interface ExtentInfo {
  aspect: number;
  coreRadiusKm: number;
  spanKm: number;
}

export interface LineScene {
  id: string;
  name: string;
  color: string;
  shapes: ShapeGeometry[];
}

export interface ShapeGeometry {
  id: string;
  points: number[];
  lengthM: number;
}

/** A station. `rank` is 1 for an ordinary station, 2 for a terminus, 3 for an interchange. */
export interface StationMarker {
  x: number;
  y: number;
  name: string;
  rank: number;
}

/** One body of water: its outline first, then one ring per island. Rings are implicitly closed. */
export interface WaterArea {
  rings: number[][];
}
