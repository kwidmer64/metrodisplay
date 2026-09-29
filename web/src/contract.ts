// Mirrors the parts of MetroDisplay.Contracts the renderer reads. Written by hand for now;
// slice 2 replaces this file with types generated from the C# records.

export interface NetworkScene {
  artifactVersion: string;
  extent: ExtentInfo;
  lines: LineScene[];
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
