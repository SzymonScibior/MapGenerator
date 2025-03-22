using System.Collections.Generic;
using UnityEngine;

public class RoomGeneration : MonoBehaviour
{
    [Header("Map Generation Settings")]
    [Tooltip("List of normal room prefabs (must have a BoxCollider and Door children)")]
    public List<GameObject> roomPrefabs;

    [Tooltip("The special generator room prefab (destination room)")]
    public GameObject generatorRoomPrefab;

    [Tooltip("The starting room prefab")]
    public GameObject startRoomPrefab;

    [Tooltip("Total number of rooms to generate (including start and generator)")]
    public int totalRooms = 10;

    [Tooltip("Tolerance for door alignment (in world units)")]
    public float doorSnapTolerance = 0.2f;

    // Internal lists to keep track of placed rooms and available door connection points.
    private List<PlacedRoom> placedRooms = new List<PlacedRoom>();
    private List<OpenDoor> openDoors = new List<OpenDoor>();

    void Start()
    {
        GenerateMap();
    }

    void GenerateMap()
    {
        // STEP 1: Place the start room at the origin.
        GameObject startRoom = Instantiate(startRoomPrefab, Vector3.zero, Quaternion.identity);
        PlacedRoom startPlacedRoom = new PlacedRoom(startRoom);
        placedRooms.Add(startPlacedRoom);
        // Collect all open door positions from the start room.
        AddRoomDoorsToOpenList(startPlacedRoom);
        Debug.LogWarning("works here");


        bool generatorPlaced = false;  // To track if the destination room is already placed.

        // STEP 2: Keep placing rooms until the desired count is reached.
        while (placedRooms.Count < totalRooms && openDoors.Count > 0)
        {
            Debug.LogWarning("works here2");

            // Randomly select one open door from our list.
            int doorIndex = Random.Range(0, openDoors.Count);
            OpenDoor currentOpenDoor = openDoors[doorIndex];

            bool roomAttached = false; 

            // STEP 3: Decide which prefab to use.
            // If we’re about to place the last room and haven’t placed the generator room yet, force it.
            GameObject candidatePrefab = null;
            if (!generatorPlaced && placedRooms.Count == totalRooms - 1)
            {
                candidatePrefab = generatorRoomPrefab;
            }
            else
            {
                // Randomly choose one of the normal room prefabs.
                candidatePrefab = roomPrefabs[Random.Range(0, roomPrefabs.Count)];
            }

            Door[] candidateDoors = candidatePrefab.GetComponentsInChildren<Door>();

            // STEP 4: Try to attach the candidate room by matching one of its doors to the current open door.
            foreach (Door candidateDoor in candidateDoors)
            {
                Debug.LogWarning("works here3");

                // The candidate door must be the “opposite” of the current open door.
                //if (IsOppositeDirection(candidateDoor.doorDirection, currentOpenDoor.doorDirection))
                //{
                // Try all four 90° rotations (0°, 90°, 180°, 270°)
                for (int i = 0; i < 4; i++)
                {
                    Debug.LogWarning("works here4");
                    Quaternion candidateRotation = Quaternion.Euler(0, 90 * i, 0);

                    Vector3 candidateDoorDir = GetDirectionVector(candidateDoor.doorDirection);
                    Vector3 rotatedCandidateDoorDir = candidateRotation * candidateDoorDir;
                    Vector3 openDoorDirVec = GetDirectionVector(currentOpenDoor.doorDirection);

                    // For a proper connection, the candidate door’s rotated direction must point exactly opposite
                    // to the open door’s direction. (We use doorSnapTolerance to allow for tiny numerical differences.)
                    if (Vector3.Distance(rotatedCandidateDoorDir, -openDoorDirVec) < doorSnapTolerance)
                    {
                        Debug.LogWarning("works here5");

                        // STEP 5: Compute the candidate room’s position.
                        // The candidate door’s local position (in the prefab) will be rotated as well.
                        Vector3 candidateDoorLocalPos = candidateDoor.transform.localPosition;
                        Vector3 rotatedCandidateDoorPos = candidateRotation * candidateDoorLocalPos;
                        Vector3 candidateRoomPos = currentOpenDoor.position - rotatedCandidateDoorPos;

                        GameObject candidateRoomInstance = Instantiate(candidatePrefab, candidateRoomPos, candidateRotation);
                        PlacedRoom candidatePlacedRoom = new PlacedRoom(candidateRoomInstance);
                        // STEP 6: Collision Check
                        // Ensure that this candidate room does not overlap any previously placed room.
                        if (!IsOverlapping(candidatePlacedRoom))
                        {
                            placedRooms.Add(candidatePlacedRoom);
                            roomAttached = true;
                            openDoors.RemoveAt(doorIndex);
                            AddRoomDoorsToOpenList(candidatePlacedRoom, candidateDoor);

                            if (candidatePrefab == generatorRoomPrefab)
                                generatorPlaced = true;
                            break;
                        }
                        else
                        {
                            // The candidate room overlapped with an existing room.
                            Destroy(candidateRoomInstance);
                        }
                    }
                }
                //}
                if (roomAttached)
                    break; // A connection was made; move to the next open door.
            }
            // If no room could be attached to this open door, remove it from the list.
            if (!roomAttached)
            {
                openDoors.RemoveAt(doorIndex);
            }
        }
    }

    // Adds all door connection points from a placed room into the openDoors list.
    // Optionally, a door (used for the connection) can be excluded.
    // param name = "placedRoom" : The room that was just placed.
    // param name = "usedDoor" : Door that was used for connection (can be null).
    void AddRoomDoorsToOpenList(PlacedRoom placedRoom, Door usedDoor = null)
    {
        // Get all Door components from the room instance.
        Door[] doors = placedRoom.roomInstance.GetComponentsInChildren<Door>();
        foreach (Door door in doors)
        {
            if (door == usedDoor)
                continue;
            Vector3 doorWorldPos = door.transform.position;
            Vector3 forward = door.transform.forward;
            DoorDirection doorDir = GetClosestDoorDirection(forward);
            OpenDoor openDoor = new OpenDoor(doorWorldPos, doorDir, placedRoom);
            openDoors.Add(openDoor);
        }
    }

    // Checks whether the candidate room (via its BoxCollider bounds) overlaps any already placed room.
    // param name = "candidate" : The candidate room to test.
    // returns : True if overlapping is detected; 
    bool IsOverlapping(PlacedRoom candidate)
    {
        // 1) Check if 'candidate' or 'candidate.roomInstance' is null
        if (candidate == null || candidate.roomInstance == null)
        {
            Debug.LogWarning("Candidate or candidate.roomInstance is null. Cannot check overlap.");
            return true; 
        }

        // 2) Attempt to get the BoxCollider on the root, then children
        BoxCollider candidateCollider = candidate.roomInstance.GetComponent<BoxCollider>();
        if (candidateCollider == null)
        {
            candidateCollider = candidate.roomInstance.GetComponentInChildren<BoxCollider>();
        }

        // 3) If still null, do not proceed
        if (candidateCollider == null)
        {
            Debug.LogWarning("Candidate room is missing a BoxCollider (neither on root nor children).");
            return true;
        }

        // 4) Access candidateCollider.bounds
        Bounds candidateBounds = candidateCollider.bounds;

        // 5) Compare against every placed room
        foreach (PlacedRoom placed in placedRooms)
        {
            if (placed == null || placed.roomInstance == null)
            {
                // If for some reason a placed room is null, skip it
                continue;
            }

            // Look for a BoxCollider in the placed room
            BoxCollider placedCollider = placed.roomInstance.GetComponent<BoxCollider>();
            if (placedCollider == null)
            {
                placedCollider = placed.roomInstance.GetComponentInChildren<BoxCollider>();
            }
            if (placedCollider == null)
            {
                Debug.LogWarning("Candidate room is missing a BoxCollider (neither on root nor children).");

            }

            // Compare bounds for overlap
            if (candidateBounds.Intersects(placedCollider.bounds))
            {
                return true; // Overlap detected
            }
        }

        // If we reach here, no overlap
        return false;
    }


    // Returns true if door direction d1 is opposite to door direction d2.
    bool IsOppositeDirection(DoorDirection d1, DoorDirection d2)
    {
        return (d1 == DoorDirection.North && d2 == DoorDirection.South) ||
               (d1 == DoorDirection.South && d2 == DoorDirection.North) ||
               (d1 == DoorDirection.East && d2 == DoorDirection.West) ||
               (d1 == DoorDirection.West && d2 == DoorDirection.East);
    }

    // Converts a DoorDirection value into its corresponding vector.
    Vector3 GetDirectionVector(DoorDirection direction)
    {
        switch (direction)
        {
            case DoorDirection.North: return Vector3.forward;
            case DoorDirection.South: return Vector3.back;
            case DoorDirection.East: return Vector3.right;
            case DoorDirection.West: return Vector3.left;
            default: return Vector3.zero;
        }
    }

    // Given a vector (the door's forward), returns the closest cardinal DoorDirection.
    DoorDirection GetClosestDoorDirection(Vector3 dir)
    {
        Vector3 n = dir.normalized;
        float dotN = Vector3.Dot(n, Vector3.forward);
        float dotS = Vector3.Dot(n, Vector3.back);
        float dotE = Vector3.Dot(n, Vector3.right);
        float dotW = Vector3.Dot(n, Vector3.left);
        float max = Mathf.Max(dotN, dotS, dotE, dotW);
        if (max == dotN) return DoorDirection.North;
        if (max == dotS) return DoorDirection.South;
        if (max == dotE) return DoorDirection.East;
        return DoorDirection.West;
    }

    // Represents a room that has been placed in the map.
    public class PlacedRoom
    {
        public GameObject roomInstance;
        public PlacedRoom(GameObject instance)
        {
            roomInstance = instance;
        }
    }

    // Represents an available door from a placed room that can be used for connection.
    public class OpenDoor
    {
        public Vector3 position;         // World position of the door.
        public DoorDirection doorDirection;  // The door's facing (as determined from its transform).
        public PlacedRoom parentRoom;      // Which room this door belongs to.

        public OpenDoor(Vector3 pos, DoorDirection dir, PlacedRoom parent)
        {
            position = pos;
            doorDirection = dir;
            parentRoom = parent;
        }
    }
}
