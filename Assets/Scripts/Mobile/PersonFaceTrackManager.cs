using System.Collections.Generic;
using UnityEngine;

public enum NewPersonPriority
{
    [InspectorName("Larger Face")]
    LargerFace,

    [InspectorName("Higher Confidence")]
    HigherConfidence,

    [InspectorName("Closest To Screen Center")]
    ClosestToScreenCenter
}

public enum PrimaryPersonSelection
{
    [InspectorName("Closest To Screen Center")]
    ClosestToScreenCenter,

    [InspectorName("Larger Face")]
    LargerFace,

    [InspectorName("Lowest Person ID")]
    LowestPersonId
}

[System.Serializable]
public struct FaceTrackCandidate
{
    public int DetectionIndex;
    public Vector2 NormalizedCenter;
    public Rect NormalizedBounds;
    public float Confidence;
    public bool HasBounds;
}

[System.Serializable]
public struct PersonFaceTrack
{
    public int PersonId;
    public string Label;
    public Vector2 NormalizedCenter;
    public Rect NormalizedBounds;
    public float Confidence;
    public bool HasBounds;
    public float LastSeenTime;
}

public interface IMultiFacePositionProvider
{
    IReadOnlyList<FaceTrackCandidate> FaceTrackCandidates { get; }
}

public class PersonFaceTrackManager : MonoBehaviour
{
    [Header("Person Tracking")]
    [InspectorName("Maximum People To Track")]
    [SerializeField] private int maximumPeopleToTrack = 2;

    [InspectorName("Person Match Distance")]
    [SerializeField] private float personMatchDistance = 0.18f;

    [InspectorName("Forget Person After Seconds")]
    [SerializeField] private float forgetPersonAfterSeconds = 1.0f;

    [InspectorName("New Person Priority")]
    [SerializeField] private NewPersonPriority newPersonPriority = NewPersonPriority.LargerFace;

    [InspectorName("Primary Person Selection")]
    [SerializeField] private PrimaryPersonSelection primaryPersonSelection = PrimaryPersonSelection.ClosestToScreenCenter;

    private readonly List<PersonFaceTrack> activeTracks = new();
    private readonly List<int> freedPersonIds = new();
    private readonly List<FaceTrackCandidate> sortedCandidates = new();
    private readonly HashSet<int> matchedTrackIndexes = new();
    private readonly HashSet<int> matchedCandidateIndexes = new();
    private PersonFaceTrack primaryPersonTrack;
    private bool hasPrimaryPersonTrack;
    private int nextPersonId = 1;
    private int lastPrimaryPersonId;

    public IReadOnlyList<PersonFaceTrack> ActiveTracks => activeTracks;
    public bool HasPrimaryPersonTrack => hasPrimaryPersonTrack;
    public PersonFaceTrack PrimaryPersonTrack => primaryPersonTrack;
    public int MaximumPeopleToTrack => Mathf.Max(1, maximumPeopleToTrack);

    private void OnValidate()
    {
        maximumPeopleToTrack = Mathf.Max(1, maximumPeopleToTrack);
        personMatchDistance = Mathf.Max(0.001f, personMatchDistance);
        forgetPersonAfterSeconds = Mathf.Max(0.01f, forgetPersonAfterSeconds);
    }

    public void UpdateTracks(IReadOnlyList<FaceTrackCandidate> candidates, float now)
    {
        ExpireOldTracks(now);
        matchedTrackIndexes.Clear();
        matchedCandidateIndexes.Clear();

        if (candidates != null)
            MatchExistingTracks(candidates, now);

        if (candidates != null)
            AddUnmatchedCandidates(candidates, now);

        LimitActiveTracks();
        UpdatePrimaryTrack();
    }

    public bool TryGetPersonTrack(int personId, out PersonFaceTrack track)
    {
        for (int i = 0; i < activeTracks.Count; i++)
        {
            if (activeTracks[i].PersonId == personId)
            {
                track = activeTracks[i];
                return true;
            }
        }

        track = default;
        return false;
    }

    public void ClearTracks()
    {
        for (int i = 0; i < activeTracks.Count; i++)
        {
            PersonFaceTrack track = activeTracks[i];
            freedPersonIds.Add(track.PersonId);
            Debug.Log($"[PersonFaceTrack] expired {track.Label} reason=clear", this);
        }

        freedPersonIds.Sort();
        activeTracks.Clear();
        hasPrimaryPersonTrack = false;
        lastPrimaryPersonId = 0;
    }

    private void MatchExistingTracks(IReadOnlyList<FaceTrackCandidate> candidates, float now)
    {
        while (true)
        {
            int bestTrackIndex = -1;
            int bestCandidateIndex = -1;
            float bestDistance = float.MaxValue;

            for (int trackIndex = 0; trackIndex < activeTracks.Count; trackIndex++)
            {
                if (matchedTrackIndexes.Contains(trackIndex))
                    continue;

                PersonFaceTrack track = activeTracks[trackIndex];
                for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    if (matchedCandidateIndexes.Contains(candidateIndex))
                        continue;

                    float distance = Vector2.Distance(track.NormalizedCenter, candidates[candidateIndex].NormalizedCenter);
                    if (distance <= personMatchDistance && distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestTrackIndex = trackIndex;
                        bestCandidateIndex = candidateIndex;
                    }
                }
            }

            if (bestTrackIndex < 0 || bestCandidateIndex < 0)
                return;

            PersonFaceTrack matchedTrack = UpdateTrack(activeTracks[bestTrackIndex], candidates[bestCandidateIndex], now);
            activeTracks[bestTrackIndex] = matchedTrack;
            matchedTrackIndexes.Add(bestTrackIndex);
            matchedCandidateIndexes.Add(bestCandidateIndex);
            Debug.Log(
                $"[PersonFaceTrack] matched {matchedTrack.Label} detectionIndex={candidates[bestCandidateIndex].DetectionIndex} distance={bestDistance:0.000}",
                this);
        }
    }

    private void AddUnmatchedCandidates(IReadOnlyList<FaceTrackCandidate> candidates, float now)
    {
        sortedCandidates.Clear();
        for (int i = 0; i < candidates.Count; i++)
        {
            if (!matchedCandidateIndexes.Contains(i))
                sortedCandidates.Add(candidates[i]);
        }

        sortedCandidates.Sort(CompareNewPersonCandidates);

        for (int i = 0; i < sortedCandidates.Count && activeTracks.Count < MaximumPeopleToTrack; i++)
        {
            int personId = AllocatePersonId();
            PersonFaceTrack track = UpdateTrack(
                new PersonFaceTrack
                {
                    PersonId = personId,
                    Label = $"P{personId}"
                },
                sortedCandidates[i],
                now);
            activeTracks.Add(track);
            Debug.Log(
                $"[PersonFaceTrack] assigned {track.Label} detectionIndex={sortedCandidates[i].DetectionIndex} center={track.NormalizedCenter} bounds={track.NormalizedBounds} confidence={track.Confidence:0.000}",
                this);
        }
    }

    private PersonFaceTrack UpdateTrack(PersonFaceTrack track, FaceTrackCandidate candidate, float now)
    {
        track.NormalizedCenter = candidate.NormalizedCenter;
        track.NormalizedBounds = candidate.NormalizedBounds;
        track.Confidence = candidate.Confidence;
        track.HasBounds = candidate.HasBounds;
        track.LastSeenTime = now;
        return track;
    }

    private void ExpireOldTracks(float now)
    {
        for (int i = activeTracks.Count - 1; i >= 0; i--)
        {
            PersonFaceTrack track = activeTracks[i];
            if (now - track.LastSeenTime <= forgetPersonAfterSeconds)
                continue;

            Debug.Log($"[PersonFaceTrack] expired {track.Label} lastSeenAge={now - track.LastSeenTime:0.000}", this);
            freedPersonIds.Add(track.PersonId);
            freedPersonIds.Sort();
            activeTracks.RemoveAt(i);
        }
    }

    private void LimitActiveTracks()
    {
        while (activeTracks.Count > MaximumPeopleToTrack)
        {
            int removeIndex = FindLowestPriorityTrackIndex();
            PersonFaceTrack track = activeTracks[removeIndex];
            freedPersonIds.Add(track.PersonId);
            freedPersonIds.Sort();
            activeTracks.RemoveAt(removeIndex);
            Debug.Log($"[PersonFaceTrack] removed {track.Label} reason=maximumPeopleToTrack", this);
        }
    }

    private int AllocatePersonId()
    {
        if (freedPersonIds.Count > 0)
        {
            int personId = freedPersonIds[0];
            freedPersonIds.RemoveAt(0);
            return personId;
        }

        return nextPersonId++;
    }

    private void UpdatePrimaryTrack()
    {
        if (activeTracks.Count == 0)
        {
            hasPrimaryPersonTrack = false;
            if (lastPrimaryPersonId != 0)
            {
                Debug.Log("[PersonFaceTrack] primary changed from=P" + lastPrimaryPersonId + " to=None", this);
                lastPrimaryPersonId = 0;
            }

            return;
        }

        int primaryIndex = 0;
        for (int i = 1; i < activeTracks.Count; i++)
        {
            if (ComparePrimary(activeTracks[i], activeTracks[primaryIndex]) < 0)
                primaryIndex = i;
        }

        primaryPersonTrack = activeTracks[primaryIndex];
        hasPrimaryPersonTrack = true;
        if (lastPrimaryPersonId != primaryPersonTrack.PersonId)
        {
            string previous = lastPrimaryPersonId == 0 ? "None" : $"P{lastPrimaryPersonId}";
            Debug.Log($"[PersonFaceTrack] primary changed from={previous} to={primaryPersonTrack.Label}", this);
            lastPrimaryPersonId = primaryPersonTrack.PersonId;
        }
    }

    private int CompareNewPersonCandidates(FaceTrackCandidate a, FaceTrackCandidate b)
    {
        int primary = newPersonPriority switch
        {
            NewPersonPriority.HigherConfidence => b.Confidence.CompareTo(a.Confidence),
            NewPersonPriority.ClosestToScreenCenter => DistanceFromScreenCenter(a.NormalizedCenter).CompareTo(DistanceFromScreenCenter(b.NormalizedCenter)),
            _ => FaceArea(b).CompareTo(FaceArea(a))
        };

        return primary != 0 ? primary : a.DetectionIndex.CompareTo(b.DetectionIndex);
    }

    private int ComparePrimary(PersonFaceTrack a, PersonFaceTrack b)
    {
        int primary = primaryPersonSelection switch
        {
            PrimaryPersonSelection.LargerFace => FaceArea(b).CompareTo(FaceArea(a)),
            PrimaryPersonSelection.LowestPersonId => a.PersonId.CompareTo(b.PersonId),
            _ => DistanceFromScreenCenter(a.NormalizedCenter).CompareTo(DistanceFromScreenCenter(b.NormalizedCenter))
        };

        return primary != 0 ? primary : a.PersonId.CompareTo(b.PersonId);
    }

    private int FindLowestPriorityTrackIndex()
    {
        int removeIndex = 0;
        for (int i = 1; i < activeTracks.Count; i++)
        {
            if (ComparePrimary(activeTracks[i], activeTracks[removeIndex]) > 0)
                removeIndex = i;
        }

        return removeIndex;
    }

    private static float FaceArea(FaceTrackCandidate candidate)
    {
        return candidate.HasBounds ? Mathf.Max(0f, candidate.NormalizedBounds.width * candidate.NormalizedBounds.height) : 0f;
    }

    private static float FaceArea(PersonFaceTrack track)
    {
        return track.HasBounds ? Mathf.Max(0f, track.NormalizedBounds.width * track.NormalizedBounds.height) : 0f;
    }

    private static float DistanceFromScreenCenter(Vector2 normalizedCenter)
    {
        return Vector2.Distance(normalizedCenter, new Vector2(0.5f, 0.5f));
    }
}
