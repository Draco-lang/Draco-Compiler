import * as d3 from "d3";
import React from "react";
import { MessageModel, ThreadModel, TraceModel } from "./Model";

type Props = {
    width: number;
    height: number;
    data: ThreadModel;
};

interface TimelineMessageModel extends MessageModel {
    isPlaceholder: boolean;
};

const TimelineGraph = (props: Props) => {
    const domRef = React.useRef(null);

    const [data, setData] = React.useState(props.data);
    const [width, setWidth] = React.useState(props.width);
    const [height, setHeight] = React.useState(props.height);

    React.useEffect(() => buildGraph(domRef, props), [data, width, height]);

    return (
        <svg ref={domRef}>
        </svg>
    );
};

function buildGraph(domRef: React.MutableRefObject<null>, props: Props) {
    const svg = d3
        .select(domRef.current)
        .attr('width', props.width)
        .attr('height', props.height);

    const messageHierarchy = d3.hierarchy(toTimelineMessage(props.data.rootMessage), getTimelineChildren);
    messageHierarchy.sum(node => node.children && node.children.length > 0 ? 0 : getTimeSpan(node));

    const partitionLayout = d3
        .partition<TimelineMessageModel>()
        .size([props.width, props.height])
        .padding(2);

    let laidOutMessages = partitionLayout(messageHierarchy);

    const colorScale = d3.interpolateHsl('green', 'red');

    // Groups of rect and text
    const allGroups = svg
        .selectAll('g')
        .data(laidOutMessages.descendants())
        .enter()
        .append('g');

    // Rects
    const allRects = allGroups
        .append('rect')
        .attr('x', node => node.x0)
        .attr('y', node => props.height - node.y1)
        .attr('width', node => node.x1 - node.x0)
        .attr('height', node => node.y1 - node.y0)
        .attr('fill', node => {
            if (node.data.isPlaceholder) return 'transparent';
            const fillPercentage = node.parent
                ? getTimeSpan(node.data) / getTimeSpan(node.parent.data)
                : 1;
            return colorScale(fillPercentage);
        });

    // Texts
    const allTexts = allGroups
        .append('text')
        .text(node => node.data.name)
        .attr('color', 'black')
        .attr('dominant-baseline', 'middle')
        .attr('text-anchor', 'middle')
        .attr('x', node => node.x0 + (node.x1 - node.x0) / 2)
        .attr('y', node => props.height - node.y1 + (node.y1 - node.y0) / 2);

    allRects.on('click', function (svg, node) {
        focus(node);

        const transition = d3
            .transition()
            .duration(500);
        allRects
            .transition(transition)
            .attr('x', (node: any) => node.target ? node.target.x0 : node.x0)
            .attr('width', (node: any) => node.target ? (node.target.x1 - node.target.x0) : (node.x1 - node.x0));
    });
}

function focus(node: d3.HierarchyRectangularNode<TimelineMessageModel>) {
    function resize(node: d3.HierarchyRectangularNode<TimelineMessageModel>, x0: number, x1: number) {
        (node as any).target = {
            x0,
            y0: node.y0,
            x1,
            y1: node.y1,
        };
    }

    // Find root
    let root = node;
    while (root.parent) root = root.parent;

    // Walk up the ancestry chain, expand
    let current = node;
    while (current.parent) {
        resize(current, root.x0, root.x1);
        current = current.parent;
    }
}

function getTimeSpan(msg: MessageModel): number {
    return msg.endTime - msg.startTime;
}

function toTimelineMessage(msg: MessageModel): TimelineMessageModel {
    return {
        ...msg,
        isPlaceholder: false,
    };
}

function getTimelineChildren(msg: MessageModel): TimelineMessageModel[] {
    function makePlaceholder(startTime: number, endTime: number): TimelineMessageModel {
        return {
            name: '',
            startTime,
            endTime,
            isPlaceholder: true,
        };
    }

    if (!msg.children || msg.children.length === 0) return [];

    const result = [];

    // Check the gap between first child and parent
    if (msg.startTime < msg.children[0].startTime) {
        // Push placeholder
        result.push(makePlaceholder(msg.startTime, msg.children[0].startTime));
    }

    for (let i = 0; i < msg.children.length; ++i) {
        // Add current message
        const child = msg.children[i];
        result.push(toTimelineMessage(child));

        // Look at next message, if there is a gap, fill it
        if (i + 1 < msg.children.length) {
            const nextChild = msg.children[i + 1];
            if (child.endTime < nextChild.startTime) {
                // Push placeholder
                result.push({
                    name: '',
                    startTime: child.endTime,
                    endTime: nextChild.startTime,
                    isPlaceholder: true,
                });
            }
        }
    }

    // Check the gap between last child and parent
    if (msg.children[msg.children.length - 1].endTime < msg.endTime) {
        // Push placeholder
        result.push(makePlaceholder(msg.children[msg.children.length - 1].endTime, msg.endTime));
    }

    return result;
}

export default TimelineGraph;
